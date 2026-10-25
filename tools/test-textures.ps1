# Checks ZDoom TEXTURES in a real run: a definition changes what is drawn.
#
#   powershell -NoProfile -File tools/test-textures.ps1
#
# The parser and the join are covered by unit tests. What only a real run can show is that the
# change reaches the frame: a definition that replaces a texture MAP01 actually uses must make
# the screenshot differ, and a definition naming a patch that does not exist must be reported
# without stopping the level.
#
# The wall textures are chosen by reading MAP01's SIDEDEFS out of the IWAD rather than by
# guessing: a texture that is not in the first room produces an identical screenshot, and a test
# that compares two identical screenshots passes for the wrong reason. That mistake was made
# once here - BROWNGRN looked like a reasonable choice and was not in view at all.

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
    Write-Host "TEXTURES test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-texdef-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

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

    $shot = Join-Path $dir "frame.bmp"

    $arguments = @('-headless', '-iwad', $Iwad, '-warp', '1', '-skill', '3',
                   '-tics', '5', '-userdir', $dir, '-nodata', '-shot', $shot) + $extra

    $output = & $Exe @arguments 2>&1

    return [pscustomobject]@{
        Text = ($output -join "`n")
        Shot = $shot
    }
}

# Reads a BMP written by the game and returns its pixels, without the header.
function Read-Bmp([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)

    $dataOffset = [BitConverter]::ToInt32($bytes, 10)

    $pixels = New-Object byte[] ($bytes.Length - $dataOffset)
    [Array]::Copy($bytes, $dataOffset, $pixels, 0, $pixels.Length)

    return $pixels
}

# The wall textures MAP01 names in its SIDEDEFS, most used first. Reading them out of the IWAD is
# what makes "the picture must change" a real assertion: the first room only shows some of them.
function Get-Map01Textures([string]$wadPath) {
    $stream = [System.IO.File]::OpenRead((Resolve-Path $wadPath))
    $reader = New-Object System.IO.BinaryReader($stream)

    try {
        $null = $reader.ReadBytes(4)
        $count = $reader.ReadInt32()
        $offset = $reader.ReadInt32()

        $stream.Position = $offset

        $lumps = @()

        for ($i = 0; $i -lt $count; $i++) {
            $position = $reader.ReadInt32()
            $size = $reader.ReadInt32()
            $name = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)).TrimEnd([char]0)

            $lumps += [pscustomobject]@{ Position = $position; Size = $size; Name = $name }
        }

        $index = -1

        for ($i = 0; $i -lt $lumps.Count; $i++) {
            if ($lumps[$i].Name -eq 'MAP01') {
                $index = $i
                break
            }
        }

        if ($index -lt 0) {
            return @()
        }

        # THINGS, LINEDEFS, SIDEDEFS: the third lump after the marker.
        $sidedefs = $lumps[$index + 3]

        $stream.Position = $sidedefs.Position
        $bytes = $reader.ReadBytes($sidedefs.Size)

        $counts = @{}

        for ($i = 0; $i -lt $bytes.Length; $i += 30) {
            foreach ($field in 4, 12, 20) {
                $name = [System.Text.Encoding]::ASCII.GetString($bytes, $i + $field, 8).TrimEnd([char]0)

                if ($name -ne '-' -and $name -ne '') {
                    if ($counts.ContainsKey($name)) {
                        $counts[$name]++
                    }
                    else {
                        $counts[$name] = 1
                    }
                }
            }
        }

        return $counts.GetEnumerator() |
            Sort-Object Value -Descending |
            Select-Object -First 12 |
            ForEach-Object { $_.Key }
    }
    finally {
        $reader.Close()
        $stream.Close()
    }
}

try {
    Write-Host "Without TEXTURES"
    $plain = Run-Game "plain" @()

    Assert-True ($plain.Text -like "*map MAP01: OK*") "the level loaded"
    Assert-True ($plain.Text -notlike "*from TEXTURES*") "and nothing came from a definition"
    Assert-True (Test-Path $plain.Shot) "a frame was written"

    $textures = Get-Map01Textures $Iwad

    Assert-True ($textures.Count -gt 0) `
        ("MAP01 names wall textures to work with (" + $textures.Count + ")")

    Write-Host "With a definition that replaces what the map uses"
    $pwad = Join-Path $work "tex.wad"

    # Every texture the first room could be showing, replaced by the same pair of door patches.
    # Doing all of them at once is deliberate: which of them is in view at tic 5 depends on where
    # the player starts facing, and a test that guessed wrong would compare two identical frames.
    $text = ""

    foreach ($name in $textures) {
        $text += "Texture $name, 128, 128`n{`n    Patch DOOR2_1, 0, 0`n    Patch DOOR2_4, 64, 0`n}`n"
    }

    New-Pwad $pwad "TEXTURES" $text

    $withTexture = Run-Game "withtexture" @('-file', $pwad)

    Assert-True ($withTexture.Text -like "*from TEXTURES*") `
        "the lump was found without a flag"
    Assert-True ($withTexture.Text -like "*map MAP01: OK*") "the level still loaded"
    Assert-True ($withTexture.Text -notlike "*missing texture patch*") `
        "every patch in the definitions exists"

    $before = Read-Bmp $plain.Shot
    $after = Read-Bmp $withTexture.Shot

    Assert-True ($before.Length -eq $after.Length) "both frames are the same size"

    $different = 0

    for ($i = 0; $i -lt $before.Length; $i++) {
        if ($before[$i] -ne $after[$i]) {
            $different++
        }
    }

    Assert-True ($different -gt 0) `
        ("the picture changed ({0} bytes differ)" -f $different)

    Write-Host "With a patch that is not in the WAD"
    $missing = Join-Path $work "missing.wad"

    New-Pwad $missing "TEXTURES" @"
Texture $($textures[0]), 128, 128
{
    Patch DOOR2_1, 0, 0
    Patch NOSUCHPATCH, 64, 0
}
"@

    $missingRun = Run-Game "missing" @('-file', $missing)

    Assert-True ($missingRun.Text -like "*NOSUCHPATCH*") "the missing patch is named"
    Assert-True ($missingRun.Text -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "With a property this port cannot draw"
    $unsupported = Join-Path $work "unsupported.wad"

    New-Pwad $unsupported "TEXTURES" @"
Texture $($textures[0]), 128, 128
{
    XScale 2.0
    Patch DOOR2_1, 0, 0
}
"@

    $unsupportedRun = Run-Game "unsupported" @('-file', $unsupported)

    Assert-True ($unsupportedRun.Text -like "*XScale*") "the unsupported property is named"
    Assert-True ($unsupportedRun.Text -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "TEXTURES test passed."
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
