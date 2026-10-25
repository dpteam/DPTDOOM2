# Checks the actor folder: written on first run, loaded on every run, and editable.
#
#   powershell -NoProfile -File tools/test-actors.ps1
#
# The feature has one job now, and it is bigger than when it started: the built-in actor table is
# text on disk, and that text is what the game uses. So the claims are:
#
#   - the folder appears on the first run and is announced once;
#   - it is loaded on every run after that, before DeHackEd, so a patch still wins;
#   - editing a number in it changes the game, with no recompiling;
#   - it is never overwritten, because the whole point is that it can be edited;
#   - -nodata neither writes nor loads it, which is what a build machine wants.
#
# The last check is the one that matters most: an unchanged folder must produce the same world as
# no folder at all. If it did not, the text would disagree with the engine it was generated from,
# and every modder copying it would start from a lie.

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
    Write-Host "Actor folder test skipped: $Iwad not found."
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

# The world state line, which is the honest observable: two runs that agree here simulated the
# same world, whatever the log says.
function State-Of([string]$text) {
    $match = [regex]::Match($text, 'State: mobj=\S+ sector=\S+ tic=\d+')
    Assert-True $match.Success "the run produced a world state"
    return $match.Value
}

# Replaces one line inside one actor's function, and nothing else.
#
# A blanket replace would also hit whatever else shares the text - "Health(20)" appears for more
# than one actor - and then the test would be checking an edit it did not make.
function Edit-Actor([string]$path, [string]$function, [string]$from, [string]$to) {
    $text = Get-Content $path -Raw

    $start = $text.IndexOf($function)
    Assert-True ($start -ge 0) "$function is in the file"

    $end = $text.IndexOf("`n}", $start)
    Assert-True ($end -gt $start) "$function has an end"

    $body = $text.Substring($start, $end - $start)
    Assert-True ($body.Contains($from)) "$function contains '$from'"

    $text = $text.Substring(0, $start) + $body.Replace($from, $to) + $text.Substring($end)
    Set-Content -Path $path -Value $text -NoNewline -Encoding ASCII
}

try {
    Write-Host "First run"
    $first = Run-Game "first" @()

    Assert-True ($first.Text -like "*map MAP01: OK*") "the level loaded"
    Assert-True ($first.Text -like "*actors: OK (4 file(s)*") "the folder was written"
    Assert-True ($first.Text -like "*actor(s))*") "and then loaded"

    Assert-True (Test-Path $first.Actors) "the folder exists"
    Assert-True (Test-Path (Join-Path $first.Actors "monsters.dpts")) "and holds the monsters"
    Assert-True (Test-Path (Join-Path $first.Actors "items.dpts")) "the items"
    Assert-True (Test-Path (Join-Path $first.Actors "decorations.dpts")) "the decorations"
    Assert-True (Test-Path (Join-Path $first.Actors "README.txt")) "and a README"

    $monsters = Get-Content (Join-Path $first.Actors "monsters.dpts") -Raw

    Assert-True ($monsters -like "*fn Thing_3004()*") "a known actor is there"
    Assert-True ($monsters -like "*Health(20)*") "with its health"
    Assert-True ($monsters -like "*SpawnState(`"PossStnd`")*") "and its states, by name"
    Assert-True ($monsters -like "*This file IS loaded*") `
        "the header says the file is loaded, and before DeHackEd"

    Write-Host "The same folder on a later run"
    # The same directory, not a fresh one: the question is whether a folder that already exists is
    # written again, and a fresh directory would answer a different question.
    $secondText = (& $Exe -headless -iwad $Iwad -warp 1 -skill 3 -tics $Tics -userdir $first.Dir 2>&1) -join "`n"

    Assert-True ($secondText -like "*actor(s))*") "it is loaded again"
    Assert-True ($secondText -notlike "*file(s)*") "but not written again"

    # The claim that makes the text trustworthy: an unchanged folder is the engine's own table.
    Assert-True ((State-Of $first.Text) -eq (State-Of $secondText)) `
        "two runs with the same folder agree"

    Write-Host "Editing the folder"
    # The claim that makes it the source of truth. A zombieman with 500 health fights differently,
    # so a run with no input at all diverges from one where it has 20.
    Edit-Actor (Join-Path $first.Actors "monsters.dpts") "fn Thing_3004()" "Health(20);" "Health(500);"

    # Started by hand rather than through Run-Game, because this run has to use the folder that was
    # just edited, and Run-Game always makes a fresh directory.
    $editedText = (& $Exe -headless -iwad $Iwad -warp 1 -skill 3 -tics $Tics -userdir $first.Dir 2>&1) -join "`n"

    Assert-True ($editedText -like "*actor(s))*") "the edited folder loaded"
    Assert-True ($editedText -notlike "*actor folder*") "with no complaints about it"

    Assert-True ((State-Of $editedText) -ne (State-Of $secondText)) `
        "the edit changed the world"

    Assert-True ((Get-Content (Join-Path $first.Actors "monsters.dpts") -Raw) -like "*Health(500)*") `
        "and the edit was not overwritten by the run"

    Write-Host "-nodata"
    $clean = Run-Game "clean" @('-nodata')

    Assert-True (-not (Test-Path $clean.Actors)) "nothing is written"
    Assert-True ($clean.Text -notlike "*actors*") "and nothing is read either"

    # And with nothing read, the compiled table is the fallback - which is the same world as an
    # unedited folder, since that is where the folder came from.
    Assert-True ((State-Of $clean.Text) -eq (State-Of $secondText)) `
        "the compiled table gives the same world as an unedited folder"

    Write-Host "Actor folder test passed."
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
