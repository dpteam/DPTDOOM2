# Checks that headless runs are reproducible.
#
#   powershell -NoProfile -File tools/test-headless.ps1
#
# Why this exists: "-tics N" used to count calls to Doom.Update rather than
# simulated game tics. The level-start wipe consumes a variable number of those
# calls (WipeEffect seeds its column heights from the clock), so the same command
# produced a different world on every launch — level time 28, 29, 32, 33, 35 out
# of five runs of "-tics 70". Any hash comparison in a test was therefore
# comparing two different moments.
#
# The fix makes "-tics" mean game tics. This script is what keeps it that way.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Tics = 200,
    [int]$Runs = 3
)

$ErrorActionPreference = "Stop"

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        Write-Host "  FAIL $message"
        throw $message
    }

    Write-Host "  ok   $message"
}

if (-not (Test-Path $Iwad)) {
    Write-Host "Headless test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

function Get-State([string]$iwad) {
    $output = & $Exe -headless -iwad $iwad -warp 1 -skill 3 -tics $Tics 2>&1
    $line = $output | Select-String -Pattern 'State: mobj='

    if (-not $line) {
        throw "No state line in the output of: $Exe -iwad $iwad"
    }

    return $line.ToString().Trim()
}

Write-Host "Determinism:"
Write-Host "  (tics=$Tics, runs=$Runs)"

$states = @()

for ($i = 1; $i -le $Runs; $i++) {
    $states += Get-State $Iwad
}

$first = $states[0]

foreach ($state in $states) {
    Assert-True ($state -eq $first) "run gives the same state: $state"
}

Assert-True ($first -match "tic=$Tics\b") "the run simulated exactly $Tics tics ($first)"

# A container must be transparent: the same WAD inside a PK3 has to produce the
# same world as the WAD on its own.
Write-Host "PK3 transparency:"

Add-Type -AssemblyName System.IO.Compression.FileSystem

$temp = Join-Path $env:TEMP ("dptdoom-headless-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $temp | Out-Null

try {
    $pk3 = Join-Path $temp "wrapped.pk3"
    $zip = [IO.Compression.ZipFile]::Open($pk3, 'Create')
    $entry = $zip.CreateEntry('doom2.wad', [IO.Compression.CompressionLevel]::NoCompression)
    $out = $entry.Open()
    $in = [IO.File]::OpenRead((Resolve-Path $Iwad))
    $in.CopyTo($out)
    $in.Dispose()
    $out.Dispose()
    $zip.Dispose()

    $inside = Get-State $pk3

    Assert-True ($inside -eq $first) "a WAD inside a PK3 gives the same world as the WAD itself"
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Headless determinism test passed."
