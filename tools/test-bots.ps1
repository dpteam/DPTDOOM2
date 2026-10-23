# Checks that the bots actually finish MAP01, in every temperament.
#
#   powershell -NoProfile -File tools/test-bots.ps1
#
# Why this exists
# ---------------
# The bots were reported as finishing MAP01 in under a minute. They did not: two
# navigation bugs made a bot explore, then decide nothing was reachable, and
# circle in the same courtyard for the rest of the run.
#
#   - "This sector is unreachable" was remembered permanently. Reachability
#     depends on where the bot stands, so one bad spot removed half the map from
#     planning, exit included.
#   - The exit was routed to as a single navigation node, which can sit in a
#     cluster of points that nothing connects to, even though the exit room is
#     reachable.
#
# Nothing tested the outcome, so both bugs survived. The unit tests only checked
# that a bot leaves the start room.
#
# This runs the real game instead of a reconstruction: the bot in the game is
# built with a brain, a temperament and a style, and an in-process bot with those
# left out does not behave the same way. Measuring the thing the player runs is
# the only honest check.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$TicLimit = 4000,
    # Slowest measured style was 2077 tics; anything near the limit means the
    # run stalled rather than explored.
    [int]$MaxFinishTics = 3200
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path $Iwad)) {
    Write-Host "Bot test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        Write-Host "  FAIL $message"
        throw $message
    }

    Write-Host "  ok   $message"
}

# 0=Rush, 1=Sweeper, 2=Hunter, 3=Cautious.
$styles = @(
    @{ Id = 0; Name = "Rush" },
    @{ Id = 1; Name = "Sweeper" },
    @{ Id = 2; Name = "Hunter" },
    @{ Id = 3; Name = "Cautious" }
)

$failures = 0

foreach ($style in $styles) {
    $output = & $Exe -headless -iwad $Iwad -bots -warp 1 -skill 3 -tics $TicLimit -botstyle $style.Id 2>&1

    $finish = $output | Select-String -Pattern 'Level MAP01 finished in (\d+) tics' | Select-Object -First 1

    if (-not $finish) {
        $last = $output | Select-String -Pattern 'player 1 pos' | Select-Object -First 1
        Write-Host ("  FAIL {0} did not finish MAP01 within {1} tics ({2})" -f $style.Name, $TicLimit, $last)
        $failures++
        continue
    }

    $tics = [int]$finish.Matches[0].Groups[1].Value

    if ($tics -gt $MaxFinishTics) {
        Write-Host ("  FAIL {0} took {1} tics, over the {2} limit" -f $style.Name, $tics, $MaxFinishTics)
        $failures++
        continue
    }

    Write-Host ("  ok   {0} finished MAP01 in {1} tics ({2}s)" -f
        $style.Name, $tics, [math]::Round($tics / 35, 1))
}

# The bot must not stop after the first level: the run continues on the next map,
# which is what the level-transition line proves.
$output = & $Exe -headless -iwad $Iwad -bots -warp 1 -skill 3 -tics $TicLimit -botstyle 0 2>&1
$maps = $output | Select-String -Pattern "Load map '(\w+)'" | ForEach-Object { $_.Matches[0].Groups[1].Value }

Assert-True ($maps.Count -ge 2) ("the run continues after MAP01 (maps loaded: " + ($maps -join ', ') + ")")

if ($failures -gt 0) {
    throw "$failures bot check(s) failed."
}

Write-Host "Bot completion test passed."
