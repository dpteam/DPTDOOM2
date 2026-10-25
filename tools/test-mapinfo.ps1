# Checks MAPINFO in a real run: a level's name, sky and music come from the file.
#
#   powershell -NoProfile -File tools/test-mapinfo.ps1
#
# The parser has its own unit tests; what only a real run can show is that the level actually uses
# the result. So this builds a PWAD holding a MAPINFO lump, starts the game on that map, and looks
# at what the engine reports and what the automap prints.
#
# The lump is written null-padded, which is the WAD format: the reader stops at the first zero, so a
# space-padded name keeps its trailing space and never matches a lookup. That is also what vanilla
# Doom does, so the strictness is faithful rather than inconvenient.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Tics = 20
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
    Write-Host "MAPINFO test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-mapinfo-" + [Guid]::NewGuid().ToString("N"))
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

    $arguments = @('-headless', '-iwad', $Iwad, '-warp', '1', '-skill', '3', '-tics', "$Tics",
                   '-userdir', $dir, '-nodata') + $extra

    $output = & $Exe @arguments 2>&1

    return [pscustomobject]@{
        Text = ($output -join "`n")
        Dir = $dir
    }
}

try {
    Write-Host "Without MAPINFO"
    $plain = Run-Game "plain" @()

    Assert-True ($plain.Text -like "*map MAP01: OK*") "the level loaded"
    Assert-True ($plain.Text -notlike "*mapinfo*") "and nothing about MAPINFO appeared"

    # The default name, which the automap prints.
    Assert-True ($plain.Text -like "*level 1: entryway*") "the engine's own name is used"

    Write-Host "With a MAPINFO lump"
    $pwad = Join-Path $work "mi.wad"

    New-Pwad $pwad "MAPINFO" @"
// A small MAPINFO: the three things a PWAD actually needs.
map MAP01 "Test Chamber"
{
    sky1 = "SKY3"
    music = "D_E1M2"
}
"@

    $withInfo = Run-Game "withinfo" @('-file', $pwad)

    Assert-True ($withInfo.Text -like "*mapinfo: OK (1 map(s) described)*") `
        "the lump was found without a flag"
    Assert-True ($withInfo.Text -like "*map MAP01: OK*") "the level still loaded"

    # The automap draws the title, so the title has to be in the render.
    Assert-True ($withInfo.Text -like "*Test Chamber*") "the new name is what the game uses"
    Assert-True ($withInfo.Text -notlike "*level 1: entryway*") "and the old one is gone"

    Write-Host "An unsupported keyword"
    $partial = Join-Path $work "partial.wad"

    New-Pwad $partial "MAPINFO" @"
cluster 1
{
    flat = "FLOOR1"
}

map MAP01 "Still Read"
"@

    $partialRun = Run-Game "partial" @('-file', $partial)

    Assert-True ($partialRun.Text -like "*map MAP01: OK*") "the level still loads"
    Assert-True ($partialRun.Text -like "*cluster*") "the unsupported keyword is named"
    Assert-True ($partialRun.Text -like "*not a MAPINFO keyword*") "and says what it is"
    Assert-True ($partialRun.Text -like "*Still Read*") `
        "while the entry after it is still applied"

    Write-Host "An unknown sky"
    $badSky = Join-Path $work "badsky.wad"

    New-Pwad $badSky "MAPINFO" @"
map MAP01
{
    sky1 = "NOSUCHSKY"
}
"@

    $badSkyRun = Run-Game "badsky" @('-file', $badSky)

    Assert-True ($badSkyRun.Text -like "*map MAP01: OK*") "the level still loads"
    Assert-True ($badSkyRun.Text -like "*NOSUCHSKY*") "the wrong name is reported"
    Assert-True ($badSkyRun.Text -like "*keeps the engine's own sky*") "and what happened instead"

    Write-Host "MAPINFO test passed."
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
