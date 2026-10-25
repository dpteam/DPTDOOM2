# Checks TERRAIN in a real run: the file is read, and a hurting floor changes the world.
#
#   powershell -NoProfile -File tools/test-terrain.ps1
#
# What a live run can show that a unit test cannot: that the lump in a PWAD is found without a
# flag, that a clean IWAD stays silent, and - the interesting one - that a terrain which hurts
# actually changes the simulated world, while a terrain that says nothing leaves it bit for bit
# identical.
#
# That last pair is the point. A check that only looks for a difference proves the file was read;
# it does not prove the difference came from the damage. Binding an empty terrain to every flat
# and getting the same world hash back is what rules out "any TERRAIN lump perturbs the world".
#
# The terrain is bound to every flat rather than to the one the player stands on: finding that flat
# needs sector lookup from the THINGS lump, and a list of flats is a plain lump scan. If the player
# is on a flat, the terrain is applied to it.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        Write-Host "  FAIL $message"
        throw $message
    }

    Write-Host "  ok   $message"
}

if (-not (Test-Path $Iwad)) {
    Write-Host "TERRAIN test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-terrain-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

# Every flat name in the WAD, read from between the F_START and F_END markers.
function Get-Flats([string]$wadPath) {
    $stream = [System.IO.File]::OpenRead((Resolve-Path $wadPath))
    $reader = New-Object System.IO.BinaryReader($stream)

    try {
        $null = $reader.ReadBytes(4)
        $count = $reader.ReadInt32()
        $offset = $reader.ReadInt32()

        $stream.Position = $offset

        $names = @()
        $inside = $false

        for ($i = 0; $i -lt $count; $i++) {
            $position = $reader.ReadInt32()
            $size = $reader.ReadInt32()
            $name = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)).TrimEnd([char]0)

            if ($name -eq 'F_START' -or $name -eq 'FF_START' -or
                $name -eq 'F1_START' -or $name -eq 'F2_START' -or $name -eq 'F3_START') {
                $inside = $true
                continue
            }

            if ($name -eq 'F_END' -or $name -eq 'FF_END' -or
                $name -eq 'F1_END' -or $name -eq 'F2_END' -or $name -eq 'F3_END') {
                $inside = $false
                continue
            }

            if ($inside -and $size -gt 0) {
                $names += $name
            }
        }

        return $names
    }
    finally {
        $reader.Close()
        $stream.Close()
    }
}

function New-Pwad([string]$path, [string]$lumpName, [string]$content) {
    $data = [System.Text.Encoding]::ASCII.GetBytes($content)

    $name = New-Object byte[] 8
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($lumpName.ToUpperInvariant())
    [Array]::Copy($bytes, $name, [Math]::Min(8, $bytes.Length))

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("PWAD"))
    $writer.Write([int32]1)
    $writer.Write([int32](12 + $data.Length))

    $writer.Write($data)
    $writer.Write([int32]12)
    $writer.Write([int32]$data.Length)
    $writer.Write($name)

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($path, $stream.ToArray())
    $writer.Close()
}

function Run-Game([string]$name, [string[]]$extra) {
    $dir = Join-Path $work $name
    New-Item -ItemType Directory -Force $dir | Out-Null

    $arguments = @('-headless', '-iwad', $Iwad, '-warp', '1', '-skill', '3',
                   '-tics', '20', '-stateat', '20', '-userdir', $dir, '-nodata') + $extra

    $output = & $Exe @arguments 2>&1

    return ($output -join "`n")
}

# The world hash printed by -stateat, or null when the run did not get that far.
function Get-WorldHash([string]$text) {
    $match = [regex]::Match($text, 'State@\d+: mobj=([0-9a-f]+) sector=([0-9a-f]+)')

    if (-not $match.Success) {
        return $null
    }

    return $match.Groups[1].Value + "/" + $match.Groups[2].Value
}

try {
    $flats = Get-Flats $Iwad

    Assert-True ($flats.Count -gt 0) ("the WAD has flats to bind (" + $flats.Count + ")")

    Write-Host "Without TERRAIN"
    $plain = Run-Game "plain" @()

    Assert-True ($plain -like "*map MAP01: OK*") "the level loaded"
    Assert-True ($plain -notlike "*terrain: OK*") "and nothing about TERRAIN appeared"

    $baseline = Get-WorldHash $plain

    Assert-True ($baseline -ne $null) "the world hash was reported"

    Write-Host "With a terrain that says nothing"
    $empty = Join-Path $work "empty.wad"

    $text = "terrain Harmless`n{`n}`n"

    foreach ($flat in $flats) {
        $text += "floor $flat Harmless`n"
    }

    New-Pwad $empty "TERRAIN" $text

    $emptyRun = Run-Game "empty" @('-file', $empty)

    Assert-True ($emptyRun -like "*terrain: OK (1 terrain(s) defined)*") `
        "the lump was found without a flag"
    Assert-True ($emptyRun -like "*map MAP01: OK*") "the level still loaded"

    $unchanged = Get-WorldHash $emptyRun

    Assert-True ($unchanged -eq $baseline) `
        "and the world is bit for bit identical (a TERRAIN lump alone changes nothing)"

    Write-Host "With a terrain that hurts"
    $hurt = Join-Path $work "hurt.wad"

    $text = @"
terrain Nukage
{
    damageamount 3
    damagetimemask 0
}

"@

    foreach ($flat in $flats) {
        $text += "floor $flat Nukage`n"
    }

    New-Pwad $hurt "TERRAIN" $text

    $hurtRun = Run-Game "hurt" @('-file', $hurt)

    Assert-True ($hurtRun -like "*map MAP01: OK*") "the level still loaded"

    $harmed = Get-WorldHash $hurtRun

    Assert-True ($harmed -ne $baseline) `
        "the world differs - the player took damage"

    Write-Host "With directives this port cannot honour"
    $unsupported = Join-Path $work "unsupported.wad"

    New-Pwad $unsupported "TERRAIN" @"
terrain T
{
    damageamount 1
    damagetype Fire
    splash S
}

splash S
{
    smallclass SplashBase
    sound water
    noalert
}

ceiling $($flats[0]) T
"@

    $unsupportedRun = Run-Game "unsupported" @('-file', $unsupported)

    Assert-True ($unsupportedRun -like "*damagetype*") "damagetype is named"
    Assert-True ($unsupportedRun -like "*smallclass*") "a particle property is named"
    Assert-True ($unsupportedRun -like "*noalert*") "noalert is named"
    Assert-True ($unsupportedRun -like "*floors only*") "a ceiling binding is named"
    Assert-True ($unsupportedRun -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "With a flat this WAD does not have"
    $missing = Join-Path $work "missing.wad"

    New-Pwad $missing "TERRAIN" "terrain T { damageamount 1 }`nfloor NOSUCHFLAT T`n"

    $missingRun = Run-Game "missing" @('-file', $missing)

    Assert-True ($missingRun -like "*NOSUCHFLAT*") "the missing flat is named"
    Assert-True ($missingRun -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "TERRAIN test passed."
}
catch {
    Write-Host ""
    Write-Host "Logs are in $work"
    throw
}
finally {
    if ($Error.Count -eq 0 -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
