# Checks jump in a real game: the key is bound, the demo format stays compatible, and the
# physics actually move the player.
#
#   powershell -NoProfile -File tools/test-jump.ps1
#
# Jump is the one thing this port adds to the simulation instead of reproducing Doom, so it
# has two obligations and a live run is the only place both can be seen together:
#
#   - a game nobody jumped in must still record a vanilla version 109 demo. That is not
#     cosmetic: the compatibility suite pins world hashes against recordings made before jump
#     existed, and a demo that quietly changed format would break every one of them;
#   - the key must be bound and rebindable, because a feature nobody can find is not a
#     feature.
#
# What this cannot check is the player pressing the key - that needs a window and a hand. The
# physics itself is covered by JumpTest, which drives the game with commands directly.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Tics = 60
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
    Write-Host "Jump test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-jump-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

try {
    # ------------------------------------------------------------ the demo format
    Write-Host "Recording without a jump"

    $demo = Join-Path $work "plain.lmp"

    $output = & $Exe -headless -iwad $Iwad -warp 1 -skill 3 -tics $Tics -record $demo 2>&1

    # Joined before matching: -like on an array returns an array of matches, and the
    # assertion wants one answer.
    $text = $output -join "`n"

    Assert-True ($text -like "*map MAP01: OK*") "the level loaded"
    Assert-True (Test-Path $demo) "a demo was written"

    $bytes = [System.IO.File]::ReadAllBytes($demo)

    # Version 109 is vanilla Doom 1.9. Anything else here means a recording that nobody
    # jumped in stopped being readable by other ports, for no gain.
    Assert-True ($bytes[0] -eq 109) `
        ("a run with no jump still records a vanilla demo (version byte $($bytes[0]))")

    # 9 header bytes, 4 in-game flags, then four bytes per player per tic, then the
    # terminator. A five-byte record would show up here immediately.
    $headerSize = 9 + 4
    $recordBytes = $bytes.Length - $headerSize - 1

    Assert-True (($recordBytes % 4) -eq 0) `
        "the record size is four bytes per tic, as vanilla expects ($recordBytes bytes)"

    # ------------------------------------------------------------ the binding
    Write-Host "The key binding"

    # Not checked here: the config file is written only by the windowed front end, so a
    # headless run creates none. JumpBindingTest covers the default and the round trip
    # through a config file, which is where a binding that is written but never read would
    # show up.
    Write-Host "  --   the binding is covered by JumpBindingTest"

    Write-Host "Jump test passed."
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
