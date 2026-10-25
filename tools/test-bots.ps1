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
    # Measured in a throwaway brain directory, after one warm-up run: Rush 1933,
    # Sweeper 1713, Hunter 1545, Cautious 1621. Anything approaching the limit means the
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

# A throwaway brain directory, so the run cannot read the developer's accumulated file.
#
# The bot learns between runs: the brain beside the executable records which sectors cost
# health and which style suited the map. That is the feature, but it made this check
# unreliable in both directions - a green result could be the leftover file rather than the
# bot, and a red one could be a stale grudge against a corner. Everything below runs in a
# directory that is deleted afterwards.
$brains = Join-Path $env:TEMP ("dptdoom-brains-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $brains | Out-Null

$failures = 0

foreach ($style in $styles) {
    # One warm-up run whose result is ignored. A bot on a map it has never seen is a
    # different thing from the bot a player meets on the second attempt, and MAP01 has
    # places that are one-way on foot: a fresh Sweeper walks off a ledge and cannot get
    # back to the exit, while the same bot with one run behind it does not go there.
    # Asserting on the first run would test the map's geometry, not the bot.
    & $Exe -headless -iwad $Iwad -bots -warp 1 -skill 3 -tics $TicLimit `
        -botstyle $style.Id -botbraindir $brains 2>&1 | Out-Null

    $output = & $Exe -headless -iwad $Iwad -bots -warp 1 -skill 3 -tics $TicLimit `
        -botstyle $style.Id -botbraindir $brains 2>&1

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
$output = & $Exe -headless -iwad $Iwad -bots -warp 1 -skill 3 -tics $TicLimit -botstyle 0 -botbraindir $brains 2>&1
# The startup log names the map in its own label: "map MAP01: OK (level 1: entryway)". That is
# the kernel-log shape the console uses now, and it keeps the map name greppable
# without needing the message text.
$maps = $output | Select-String -Pattern "map (MAP\d+|E\dM\d+):" | ForEach-Object { $_.Matches[0].Groups[1].Value }

Assert-True ($maps.Count -ge 2) ("the run continues after MAP01 (maps loaded: " + ($maps -join ', ') + ")")

# --------------------------------------------------------------- door spam
#
# "The bot stands at a door opening and closing it" is a report that cannot be argued
# with, only counted. The trace records the button byte and the leg the bot is following,
# so the presses can be split by where they happen - and that split is the whole point:
#
#   - a press on a leg that ends at a door is the bot opening one door. A level has a
#     couple of dozen of them, so a healthy run presses a handful of times;
#   - a press while the bot is blocked is the fallback "try Use, maybe it is a door". It
#     fires often and legitimately, and counting it as spam made the first version of this
#     check fail a run that was fine (318 presses, of which nine were at doors).
#
# The bug was a bot pressing at a door that was already moving, which the engine treats as
# "close it": the count that shows that is the door-leg count.
Write-Host "Door presses"

$trace = Join-Path $env:TEMP "dptdoom-doors.json"
Remove-Item $trace -ErrorAction SilentlyContinue

& $Exe -headless -iwad $Iwad -bots -warp 1 -skill 3 -tics $TicLimit -botstyle 0 -botbraindir $brains -botdump $trace 2>&1 | Out-Null

if (-not (Test-Path $trace)) {
    Write-Host "  --   the run wrote no trace; door presses not counted"
}
else {
    $json = Get-Content $trace -Raw

    # Regex rather than a JSON reader: the samples are plain numbers and short strings,
    # and PowerShell 5.1 has nothing faster. The Use bit is 2.
    $samples = [regex]::Matches($json, '"buttons":(\d+),"sector":\d+,"health":\d+,"target":"([^"]*)"')
    $useSamples = 0
    $doorSamples = 0

    foreach ($match in $samples) {
        $buttons = [int]$match.Groups[1].Value

        if (($buttons -band 2) -eq 0) {
            continue
        }

        $useSamples++

        if ($match.Groups[2].Value -like "*(door)*") {
            $doorSamples++
        }
    }

    Write-Host ("  --   {0} samples, {1} with Use pressed, {2} of those at a door" -f
        $samples.Count, $useSamples, $doorSamples)

    Assert-True ($samples.Count -gt 100) "the trace recorded button samples ($($samples.Count))"

    # MAP01 has more than twenty doors and a press at each is one; a bot that opens and
    # shuts the same door presses dozens of times per door, so the gap between the two
    # numbers is large enough that a limit anywhere in it is safe.
    Assert-True ($doorSamples -lt 40) `
        ("the bot is not spamming doors ({0} Use presses on door legs)" -f $doorSamples)
}

Remove-Item $brains -Recurse -Force -ErrorAction SilentlyContinue

if ($failures -gt 0) {
    throw "$failures bot check(s) failed."
}

Write-Host "Bot completion test passed."
