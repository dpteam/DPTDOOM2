# Checks that a SharpScript file changes the actor it describes, in a real run.
#
#   powershell -NoProfile -File tools/test-scripts.ps1
#
# The unit tests cover the language and the API in isolation. What can only be checked
# with the real game is the wiring: that -script is read before the level is built, that
# the change reaches the actor table the world uses, and that a broken script leaves the
# game playable instead of stopping it.
#
# The observable is the world hash. A script that really doubles the zombieman's health
# makes the run diverge from the same run without it - which is the honest proof that
# something changed, rather than trusting a log line.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Tics = 120
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
    Write-Host "Script test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-scripts-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

function Run-Game([string]$name, [string[]]$extra) {
    $out = Join-Path $work ($name + ".txt")
    $arguments = @('-headless', '-iwad', $Iwad, '-warp', '1', '-skill', '3', '-tics', "$Tics") + $extra

    & $Exe @arguments 2>&1 | Set-Content -Path $out -Encoding UTF8
    return Get-Content $out -Raw
}

function State-Of([string]$log) {
    $match = [regex]::Match($log, 'State: mobj=(\S+) sector=(\S+)')
    Assert-True $match.Success "the run produced a world state"
    return $match.Groups[1].Value
}

try {
    # A script that changes what the player can see: the zombieman's health, doubled.
    $good = Join-Path $work "good.dpts"
    @"
fn Thing_3004() {
    Health(Health() * 2);
}
"@ | Set-Content -Path $good -Encoding UTF8

    # A script with a mistake in the middle, and a correct actor after it.
    $broken = Join-Path $work "broken.dpts"
    @"
fn Thing_3004() {
    Health(NoSuchFunction());
}

fn Thing_3001() {
    Health(99);
}
"@ | Set-Content -Path $broken -Encoding UTF8

    Write-Host "Baseline without a script"
    $plain = Run-Game "plain" @()
    Assert-True ($plain -like "*scripts*" -eq $false) "no script means no script line in the log"
    $plainState = State-Of $plain

    Write-Host "With a script"
    $withScript = Run-Game "good" @('-script', $good)

    Assert-True ($withScript -like "*scripts: OK*") "the script loaded"
    Assert-True ($withScript -like "*1 actor*") "and described one actor"

    $scriptState = State-Of $withScript
    Assert-True ($scriptState -ne $plainState) `
        "the world differs from the same run without the script ($plainState vs $scriptState)"

    # Same script twice must give the same world: the definitions are applied before the
    # level is built, so nothing about them can depend on time or ordering.
    $again = Run-Game "good2" @('-script', $good)
    Assert-True ((State-Of $again) -eq $scriptState) "the same script produces the same world"

    Write-Host "With a broken script"
    $withBroken = Run-Game "broken" @('-script', $broken)

    # The game must still run: a mistake in a mod costs that actor's behaviour, not the
    # level. If this ever starts exiting non-zero, one bad file has taken the game down.
    Assert-True ($withBroken -like "*map MAP01: OK*") "the level still loads"
    Assert-True ($withBroken -like "*script problem*") "the problem is reported"
    Assert-True ($withBroken -like "*is not a function*") "with the reason"
    Assert-True ($withBroken -like "*broken.dpts*") "and the file it came from"

    # And the actor after the mistake still applied, which is the point of continuing.
    $brokenState = State-Of $withBroken
    Assert-True ($brokenState -ne $plainState) `
        "the healthy part of a broken script still applied ($plainState vs $brokenState)"

    Write-Host "With a missing script file"
    $missing = Run-Game "missing" @('-script', (Join-Path $work "nope.dpts"))
    Assert-True ($missing -like "*map MAP01: OK*") "a missing file does not stop the game"
    Assert-True ($missing -like "*nope.dpts*") "and it is named in the log"

    Write-Host "Script test passed."
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
