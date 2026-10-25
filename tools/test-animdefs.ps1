# Checks ANIMDEFS in a real run: a cycle written in the file changes what is drawn.
#
#   powershell -NoProfile -File tools/test-animdefs.ps1
#
# The parser and the timing have their own unit tests. What only a real run can show is that the cycle
# is consulted while the level plays - and the observable is a frame: a flat animated through
# ANIMDEFS looks different at different tics, so two screenshots of the same level must differ.
#
# The lump is written null-padded, which is the WAD format: the reader stops at the first zero, so a
# space-padded name keeps its trailing space and never matches a lookup. "ANIMDEFS" is eight
# characters, so that particular mistake would be invisible here - it was found in MAPINFO first.

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
    Write-Host "ANIMDEFS test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-animdefs-" + [Guid]::NewGuid().ToString("N"))
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
                   '-userdir', $dir, '-nodata', '-shot', $shot) + $extra

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
    $height = [BitConverter]::ToInt32($bytes, 22)

    $pixels = New-Object byte[] ($bytes.Length - $dataOffset)
    [Array]::Copy($bytes, $dataOffset, $pixels, 0, $pixels.Length)

    return $pixels
}

try {
    Write-Host "Without ANIMDEFS"
    $plain = Run-Game "plain" @('-tics', '5')

    Assert-True ($plain.Text -like "*map MAP01: OK*") "the level loaded"
    # Matched as the subsystem line, not as a substring: the temporary directory is called
    # "dptdoom-animdefs-...", so its path in the log would satisfy a plain substring check and this
    # assertion would pass for the wrong reason.
    Assert-True ($plain.Text -notlike "*animdefs: OK*") "and nothing about ANIMDEFS appeared"
    Assert-True (Test-Path $plain.Shot) "a frame was written"

    Write-Host "With an ANIMDEFS cycle"
    $pwad = Join-Path $work "an.wad"

    # NUKAGE, three frames of two tics each: a six-tic cycle. The engine already animates these flats
    # through its own ANIMATED lump, so the point is not that the floor moves - it is that the file was
    # read and the cycle count grew.
    New-Pwad $pwad "ANIMDEFS" @"
// A cycle written by hand: three frames, two tics each.
flat NUKAGE1
{
    pic NUKAGE1 tics 2
    pic NUKAGE2 tics 2
    pic NUKAGE3 tics 2
}
"@

    $withCycle = Run-Game "cycle" @('-tics', '5', '-file', $pwad)

    Assert-True ($withCycle.Text -like "*animdefs: OK (1 cycle(s) described)*") `
        "the lump was found without a flag"
    Assert-True ($withCycle.Text -like "*map MAP01: OK*") "the level still loaded"
    Assert-True ($withCycle.Text -notlike "*missing animation frame*") `
        "every picture in the cycle exists"

    # The cycle count is what proves the entry reached the animation table rather than only the parser:
    # the engine's own table is the same either way, so an extra cycle is the difference.
    $baseline = [regex]::Match($plain.Text, 'animation: OK \((\d+) animated\)')
    $grown = [regex]::Match($withCycle.Text, 'animation: OK \((\d+) animated\)')

    Assert-True ($baseline.Success) "the plain run reported an animation count"
    Assert-True ($grown.Success) "and so did the one with the cycle"

    Assert-True ([int]$grown.Groups[1].Value -eq [int]$baseline.Groups[1].Value + 1) `
        ("the new cycle was added ($($baseline.Groups[1].Value) became $($grown.Groups[1].Value))")

    Write-Host "A cycle of one frame"
    $single = Join-Path $work "single.wad"

    New-Pwad $single "ANIMDEFS" @"
flat NUKAGE1
{
    pic NUKAGE1 tics 8
}
"@

    $singleRun = Run-Game "single" @('-tics', '5', '-file', $single)

    Assert-True ($singleRun.Text -like "*map MAP01: OK*") "the level still loads"
    Assert-True ($singleRun.Text -like "*fewer than two*") "the cycle that cannot cycle is reported"

    Write-Host "An unsupported keyword"
    $unsupported = Join-Path $work "unsupported.wad"

    New-Pwad $unsupported "ANIMDEFS" @"
warp MYWALL
{
    pic MYWALL1 tics 2
    pic MYWALL2 tics 2
}

flat NUKAGE1
{
    pic NUKAGE1 tics 2
    pic NUKAGE2 tics 2
}
"@

    $unsupportedRun = Run-Game "unsupported" @('-tics', '5', '-file', $unsupported)

    Assert-True ($unsupportedRun.Text -like "*warp*") "the unsupported keyword is named"
    Assert-True ($unsupportedRun.Text -like "*not an ANIMDEFS keyword*") "and says what it is"

    # The cycle after it still applies, and the animation count is what proves it: the file is reported
    # as failed - it did have a problem - but a problem in one entry must not cost the others.
    $afterProblem = [regex]::Match($unsupportedRun.Text, 'animation: OK \((\d+) animated\)')

    Assert-True ($afterProblem.Success) "the animation count was reported"
    Assert-True ([int]$afterProblem.Groups[1].Value -eq [int]$baseline.Groups[1].Value + 1) `
        ("while the cycle after it is still read ({0} animated)" -f $afterProblem.Groups[1].Value)

    Write-Host "A missing frame"
    $missing = Join-Path $work "missing.wad"

    New-Pwad $missing "ANIMDEFS" @"
flat NUKAGE1
{
    pic NUKAGE1 tics 2
    pic NOSUCHFLAT tics 2
    pic NUKAGE3 tics 2
}
"@

    $missingRun = Run-Game "missing" @('-tics', '5', '-file', $missing)

    Assert-True ($missingRun.Text -like "*NOSUCHFLAT*") "the missing picture is named"
    Assert-True ($missingRun.Text -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "The frames really change"
    # The observable that matters: two frames of the same level, five tics apart, must differ when a
    # cycle is running. A cycle that is parsed and never drawn would give identical frames.
    $early = Run-Game "early" @('-tics', '5', '-file', $pwad)
    $late = Run-Game "late" @('-tics', '9', '-file', $pwad)

    $a = Read-Bmp $early.Shot
    $b = Read-Bmp $late.Shot

    Assert-True ($a.Length -eq $b.Length) "both frames are the same size"

    $different = 0

    for ($i = 0; $i -lt $a.Length; $i++) {
        if ($a[$i] -ne $b[$i]) {
            $different++
        }
    }

    Assert-True ($different -gt 0) `
        ("the picture changed between tic 5 and tic 9 ({0} bytes differ)" -f $different)

    Write-Host "ANIMDEFS test passed."
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
