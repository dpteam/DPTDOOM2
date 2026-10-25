# Checks the actor reference that the game unpacks on first run.
#
#   powershell -NoProfile -File tools/test-actors.ps1
#
# The feature has one job: write the built-in actor table out as SharpScript so a modder can read
# it and copy a working actor instead of guessing at every field. That makes three claims worth
# checking from outside the process:
#
#   - the files appear on the first run, and only then;
#   - a second run does not overwrite them, because the whole point is that they can be edited;
#   - the file, fed back in with -script, is a no-op. That is the claim that makes it a
#     reference rather than a guess: if the generated patch did not reproduce the table exactly,
#     a modder would copy it, change one number, and get a result they could not explain.
#
# The last one is checked by the world hash, not by comparing text: the same run with and without
# the generated file must reach the same state.

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
    Write-Host "Actor reference test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-actors-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

function Run-Game([string]$name, [string[]]$extra) {
    $dir = Join-Path $work $name
    New-Item -ItemType Directory -Force $dir | Out-Null

    $arguments = @('-headless', '-iwad', $Iwad, '-warp', '1', '-skill', '3', '-tics', "$Tics",
                   '-userdir', $dir) + $extra

    $output = & $Exe @arguments 2>&1

    return [pscustomobject]@{
        Text = ($output -join "`n")
        Dir = $dir
        Actors = Join-Path $dir "dptdoom-data\actors"
    }
}

try {
    Write-Host "First run"
    $first = Run-Game "first" @()

    Assert-True ($first.Text -like "*map MAP01: OK*") "the level loaded"
    Assert-True ($first.Text -like "*actors: OK*") "the reference was written"

    Assert-True (Test-Path $first.Actors) "the folder exists"
    Assert-True (Test-Path (Join-Path $first.Actors "monsters.dpts")) "and holds the monsters"
    Assert-True (Test-Path (Join-Path $first.Actors "items.dpts")) "the items"
    Assert-True (Test-Path (Join-Path $first.Actors "decorations.dpts")) "the decorations"
    Assert-True (Test-Path (Join-Path $first.Actors "README.txt")) "and a README"

    $monsters = Get-Content (Join-Path $first.Actors "monsters.dpts") -Raw

    # The shotgun guy, by the numbers a player would recognise.
    Assert-True ($monsters -like "*fn Thing_3004()*") "a known actor is there"
    Assert-True ($monsters -like "*Health(20)*") "with its health"
    Assert-True ($monsters -like "*SpawnState(`"PossStnd`")*") "and its states, by name"

    # A file that mentions a secret or a guess rather than the table would be useless.
    Assert-True ($monsters -like "*This file is a REFERENCE*") `
        "the header says what the file is"

    Write-Host "Second run"
    $edited = Join-Path $first.Actors "monsters.dpts"
    Set-Content -Path $edited -Value "// edited by the test`r`n" -Encoding ASCII

    $second = Run-Game "second" @()

    # The second run has its own directory, so it unpacks its own copy. What matters is the
    # behaviour inside one directory, which is what the next block checks.
    Assert-True ($second.Text -like "*actors: OK*") "a fresh directory unpacks"

    $again = & $Exe -headless -iwad $Iwad -warp 1 -skill 3 -tics 5 -userdir $first.Dir 2>&1
    $againText = $again -join "`n"

    Assert-True ($againText -notlike "*actors: OK*") "a second run in the same directory is quiet"
    Assert-True ((Get-Content $edited -Raw) -like "*edited by the test*") `
        "and the edit survives"

    Write-Host "-nodata"
    $clean = Run-Game "clean" @('-nodata')

    Assert-True (-not (Test-Path $clean.Actors)) "nothing is written"
    Assert-True ($clean.Text -notlike "*actors: OK*") "and nothing is announced"

    Write-Host "The generated file is a no-op"
    # The claim that makes the reference trustworthy. The same run, with the generated patch fed
    # back in, must reach the same world.
    #
    # Taken from the second directory, not the first: the first one's monsters.dpts was
    # overwritten above to check that edits survive, and loading that would test the edit rather
    # than the generator.
    $plain = Run-Game "plain" @('-nodata')

    # One -script flag with several paths: the parser takes everything after the flag until the
    # next option, so repeating the flag would only keep the first one.
    $replayed = Run-Game "replayed" @('-nodata', '-script',
                                      (Join-Path $second.Actors "monsters.dpts"),
                                      (Join-Path $second.Actors "items.dpts"),
                                      (Join-Path $second.Actors "decorations.dpts"))

    Assert-True ($replayed.Text -like "*scripts: OK*") "the generated scripts loaded"
    Assert-True ($replayed.Text -notlike "*script problem*") "with no problems"

    $stateA = [regex]::Match($plain.Text, 'State: mobj=\S+ sector=\S+ tic=\d+')
    $stateB = [regex]::Match($replayed.Text, 'State: mobj=\S+ sector=\S+ tic=\d+')

    Assert-True $stateA.Success "the plain run reported a world state"
    Assert-True $stateB.Success "and so did the one with the reference applied"

    Assert-True ($stateA.Value -eq $stateB.Value) `
        ("the generated patch changes nothing (" + $stateA.Value + " vs " + $stateB.Value + ")")

    Write-Host "Actor reference test passed."
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
