# Checks that a DECORATE file reaches the real game.
#
#   powershell -NoProfile -File tools/test-decorate.ps1
#
# The unit tests cover the reading and the writing in isolation. What only a real run can
# show is the wiring: that a .dec file is converted before the level is built, that the
# numbers it carries reach the actor the world uses, and that the parts this engine cannot
# accept are reported instead of silently dropped.
#
# The observable is the world state line. A DECORATE block that really changes the imp's
# health makes the run diverge from the same run without it, which is proof that something
# happened rather than a log line saying it did.
#
# What is deliberately *not* asserted, because it would be a lie: that a States section
# worked. It does not work yet, and the test checks the opposite - that the log says so, in
# the modder's own terms, with the file and the line.

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
    Write-Host "DECORATE test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-decorate-" + [Guid]::NewGuid().ToString("N"))
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

# Writes a minimal PWAD holding one lump.
#
# The lump path matters more than the file path: a mod ships its DECORATE inside the PWAD it
# already ships, and telling the player to also pass -decorate is telling half of them to get
# it wrong. Building the WAD here rather than committing one keeps the test self-contained -
# and the format is four fields, so writing it is shorter than explaining why not to.
function New-Pwad([string]$path, [string]$lumpName, [string]$content) {
    $data = [System.Text.Encoding]::ASCII.GetBytes($content)
    # Null-padded, not space-padded: the WAD reader stops at the first zero, so a space-padded
    # name keeps its trailing space and never matches a lookup. "DECORATE" happens to be exactly
    # eight characters, which is why this was not caught here first.
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

try {
    # A block that changes nothing but numbers. This is the shape that must work.
    $numbers = Join-Path $work "numbers.dec"
    @"
// A tougher imp, and nothing else.
ACTOR TestImp 3001
{
  Health 400
  Speed 6
  PainChance 20
  +FLOAT
}
"@ | Set-Content -Path $numbers -Encoding ASCII

    # A block that also asks for states.
    $withStates = Join-Path $work "states.dec"
    @"
ACTOR TestImp 3001
{
  Health 400
  States
  {
  Spawn:
    TROO A -1 A_Look
    Loop
  See:
    TROO AB 2 A_Look
    Loop
  }
}
"@ | Set-Content -Path $withStates -Encoding ASCII

    # A doomednum this engine does not have, with a parent to inherit from. This is a new
    # monster, and it must be created rather than refused: a mod that only changes existing
    # actors cannot add anything.
    $newActor = Join-Path $work "new.dec"
    @"
ACTOR BrandNew : DoomImp
{
  spawnid 13579
  health 300
  speed 14
  States
  {
  Spawn:
    TROO AB 2 A_Look
    Loop
  }
}
"@ | Set-Content -Path $newActor -Encoding ASCII

    # A file cut short. The game must stay playable.
    $truncated = Join-Path $work "cut.dec"
    "ACTOR TestImp 3001`n{`n  Health 400" | Set-Content -Path $truncated -Encoding ASCII

    Write-Host "Baseline without DECORATE"
    $plain = Run-Game "plain" @()
    Assert-True ($plain -notlike "*decorate*") "no DECORATE means no DECORATE line in the log"
    $plainState = State-Of $plain

    Write-Host "With numbers only"
    $withNumbers = Run-Game "numbers" @('-decorate', $numbers)

    Assert-True ($withNumbers -like "*scripts: OK*") "the converted script loaded"
    Assert-True ($withNumbers -like "*1 actor*") "and described one actor"
    Assert-True ($withNumbers -notlike "*decorate problem*") `
        "a block this engine can take is converted with nothing to report"

    $numbersState = State-Of $withNumbers
    Assert-True ($numbersState -ne $plainState) `
        "the numbers reached the world ($plainState vs $numbersState)"

    # Same input twice must give the same world: conversion happens before the level is
    # built and cannot depend on anything but the text.
    $again = Run-Game "numbers2" @('-decorate', $numbers)
    Assert-True ((State-Of $again) -eq $numbersState) "the same file produces the same world"

    Write-Host "With a States section"
    $withStatesLog = Run-Game "states" @('-decorate', $withStates)

    Assert-True ($withStatesLog -like "*map MAP01: OK*") "the level still loads"
    Assert-True ($withStatesLog -like "*scripts: OK*") "the converted script loaded"
    Assert-True ($withStatesLog -notlike "*decorate problem*") `
        "a States section this engine can take is converted with nothing to report"

    # The frames must reach the world, not merely the table: the imp's own animation is
    # replaced, so a run without any input diverges from the same run without the file.
    Assert-True ((State-Of $withStatesLog) -ne $plainState) `
        "the new states changed the world ($plainState vs $(State-Of $withStatesLog))"
    Assert-True ((State-Of $withStatesLog) -ne $numbersState) `
        "and changed it differently than the numbers alone"

    Write-Host "With a States section this engine cannot take"
    $badStates = Join-Path $work "badstates.dec"
    @"
ACTOR TestImp 3001
{
  Health 400
  States
  {
  See:
    TROO A 3 A_Jump
    Loop
  }
}
"@ | Set-Content -Path $badStates -Encoding ASCII

    $badLog = Run-Game "badstates" @('-decorate', $badStates)

    Assert-True ($badLog -like "*map MAP01: OK*") "the level still loads"
    Assert-True ($badLog -like "*A_Jump*") "the action this engine lacks is named"
    Assert-True ($badLog -like "*A_Look*") "and the ones it has are listed"

    Write-Host "With a new actor"
    $newLog = Run-Game "new" @('-decorate', $newActor)

    Assert-True ($newLog -like "*map MAP01: OK*") "the level still loads"
    Assert-True ($newLog -like "*scripts: OK*") "the new actor was created rather than refused"
    Assert-True ($newLog -notlike "*not created*") "nothing says it was refused"

    Write-Host "With a DECORATE lump inside a PWAD"
    $pwad = Join-Path $work "lump.wad"
    New-Pwad $pwad "DECORATE" @"
ACTOR LumpImp 3001
{
  Health 400
  Speed 6
  PainChance 20
  +FLOAT
}
"@

    $lumpLog = Run-Game "lump" @('-file', $pwad)

    Assert-True ($lumpLog -like "*scripts: OK*") "the lump was found without a flag"
    Assert-True ($lumpLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ((State-Of $lumpLog) -eq $numbersState) `
        "and it produced the same world as passing the same text by path"

    Write-Host "With sounds named by the file"
    $sounds = Join-Path $work "sounds.dec"

    # A sound is a *name*, and since names exist this converts instead of being reported as
    # unsupported. That is what can be checked without a sound device: the file applies, the
    # report has nothing to say about it, and the world is unchanged - a sound is presentation,
    # and if it moved the world hash something would be wrong.
    [System.IO.File]::WriteAllText($sounds, @"
ACTOR SoundImp 3001
{
  SeeSound "imp/sight"
  DeathSound "imp/death"
  PainSound "imp/pain"
}
"@)

    $soundLog = Run-Game "sounds" @('-decorate', $sounds)

    Assert-True ($soundLog -like "*scripts: OK*") "the converted script loaded"
    Assert-True ($soundLog -notlike "*has no counterpart*") `
        "a sound is no longer refused"
    Assert-True ($soundLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ((State-Of $soundLog) -eq $plainState) `
        "and naming sounds did not move the world ($plainState vs $(State-Of $soundLog))"

    Write-Host "With an A_Jump this engine can take"
    $jumpWith = Join-Path $work "jump.dec"
    $jumpWithout = Join-Path $work "nojump.dec"

    # Two files that differ in exactly one thing: whether the second Spawn frame jumps. Comparing
    # them is what makes the check mean something - comparing against vanilla would only prove
    # that replacing a chain changes the world, which it does with or without a jump.
    [System.IO.File]::WriteAllText($jumpWith, @"
ACTOR JumpingImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    TROO B 4 A_Jump(256, "See")
  See:
    TROO C 4 A_Chase
    Goto See
  }
}
"@)

    [System.IO.File]::WriteAllText($jumpWithout, @"
ACTOR JumpingImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    TROO B 4
  See:
    TROO C 4 A_Chase
    Goto See
  }
}
"@)

    $jumpLog = Run-Game "jump" @('-decorate', $jumpWith)
    $noJumpLog = Run-Game "nojump" @('-decorate', $jumpWithout)

    Assert-True ($jumpLog -like "*scripts: OK*") "the converted script loaded"
    Assert-True ($jumpLog -notlike "*no such action*") "A_Jump is no longer a missing action"
    Assert-True ($jumpLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ($noJumpLog -like "*map MAP01: OK*") "and so does the same file without the jump"

    Assert-True ((State-Of $jumpLog) -ne (State-Of $noJumpLog)) `
        ("the jump moved the world: with $(State-Of $jumpLog), without $(State-Of $noJumpLog)")

    Write-Host "With an A_JumpIf this engine can take"
    $jumpIfTrue = Join-Path $work "jumpif.dec"
    $jumpIfFalse = Join-Path $work "jumpifnot.dec"

    # Two files identical except for the threshold. The imp's health is 60 in the IWAD, so one
    # condition is true and the other false - and the two worlds must differ. A single file would
    # only show that the condition was read, not that its answer decides the jump.
    [System.IO.File]::WriteAllText($jumpIfTrue, @"
ACTOR ConditionalImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    TROO B 4 A_JumpIf(health > 10, "See")
  See:
    TROO C 4 A_Chase
    Goto See
  }
}
"@)

    [System.IO.File]::WriteAllText($jumpIfFalse, @"
ACTOR ConditionalImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    TROO B 4 A_JumpIf(health > 100000, "See")
  See:
    TROO C 4 A_Chase
    Goto See
  }
}
"@)

    $trueLog = Run-Game "jumpif" @('-decorate', $jumpIfTrue)
    $falseLog = Run-Game "jumpifnot" @('-decorate', $jumpIfFalse)

    Assert-True ($trueLog -like "*scripts: OK*") "the converted script loaded"
    Assert-True ($trueLog -notlike "*no such action*") "A_JumpIf is no longer a missing action"
    Assert-True ($trueLog -like "*map MAP01: OK*") "the level loads"

    Assert-True ((State-Of $trueLog) -ne (State-Of $falseLog)) `
        ("the answer decided the jump: true $(State-Of $trueLog), false $(State-Of $falseLog)")

    Write-Host "With an A_JumpIf on a field this engine does not have"
    $jumpIfBad = Join-Path $work "jumpifbad.dec"

    [System.IO.File]::WriteAllText($jumpIfBad, @"
ACTOR CountingImp 3001
{
  States
  {
  See:
    TROO A 4 A_JumpIf(CountInv("Clip") > 0, "See")
    TROO B 4
    Loop
  }
}
"@)

    $badLog = Run-Game "jumpifbad" @('-decorate', $jumpIfBad)

    Assert-True ($badLog -like "*CountInv*") "the unknown field is named"
    Assert-True ($badLog -like "*jump is dropped*") "and only the jump is dropped"
    Assert-True ($badLog -like "*map MAP01: OK*") "the level still loads"

    Write-Host "With A_JumpIfHealthLower and A_PlaySound"
    $argLow = Join-Path $work "arglow.dec"
    $argHigh = Join-Path $work "arghigh.dec"
    $argSound = Join-Path $work "argsound.dec"

    # The same imp with the same chain; only the threshold differs. One condition is true for a
    # 60-health imp and the other is not, so the worlds must differ - which is the only way to
    # show that the answer, not the file, decided the jump.
    [System.IO.File]::WriteAllText($argLow, @"
ACTOR ArgImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    TROO B 4 A_JumpIfHealthLower(10, "See")
  See:
    TROO C 4 A_Chase
    Goto See
  }
}
"@)

    [System.IO.File]::WriteAllText($argHigh, @"
ACTOR ArgImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    TROO B 4 A_JumpIfHealthLower(100000, "See")
  See:
    TROO C 4 A_Chase
    Goto See
  }
}
"@)

    $lowLog = Run-Game "arglow" @('-decorate', $argLow)
    $highLog = Run-Game "arghigh" @('-decorate', $argHigh)

    Assert-True ($lowLog -like "*scripts: OK*") "A_JumpIfHealthLower converts"
    Assert-True ($lowLog -notlike "*no such action*") "and is no longer a missing action"
    Assert-True ((State-Of $lowLog) -ne (State-Of $highLog)) `
        ("the threshold decided the jump: low $(State-Of $lowLog), high $(State-Of $highLog)")

    # A sound is presentation, so a file that only adds one must leave the world bit for bit as a
    # file that adds nothing. If these differed, adding a sound to a mod would break every demo
    # recorded with it.
    [System.IO.File]::WriteAllText($argSound, @"
ACTOR SoundOnlyImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    TROO B 4 A_PlaySound("dspistol")
  See:
    TROO C 4 A_Chase
    Goto See
  }
}
"@)

    $silent = Join-Path $work "argsilent.dec"

    [System.IO.File]::WriteAllText($silent, @"
ACTOR SoundOnlyImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    TROO B 4
  See:
    TROO C 4 A_Chase
    Goto See
  }
}
"@)

    $soundOnlyLog = Run-Game "argsound" @('-decorate', $argSound)
    $silentLog = Run-Game "argsilent" @('-decorate', $silent)

    Assert-True ($soundOnlyLog -like "*scripts: OK*") "A_PlaySound converts"
    Assert-True ($soundOnlyLog -notlike "*takes no arguments*") "and is no longer refused"
    Assert-True ((State-Of $soundOnlyLog) -eq (State-Of $silentLog)) `
        ("the same actor without the sound gives the same world: $(State-Of $soundOnlyLog)")

    Write-Host "With a projectile a frame fires"
    $missile = Join-Path $work "missile.dec"

    # A monster that fires an imp ball from its See chain. The world must change, and for the right
    # reason: a spawn is simulation, so the projectile appears at the same tic on every end of a
    # session - unlike a sound, which is presentation and leaves the world alone.
    [System.IO.File]::WriteAllText($missile, @"
ACTOR ShooterImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    TROO B 4 A_Jump(256, "See")
  See:
    TROO C 2 A_Chase
    TROO C 2 A_CustomMissile("DoomImpBall", 32, 0, 0)
    Goto See
  }
}
"@)

    $missileLog = Run-Game "missile" @('-decorate', $missile)

    Assert-True ($missileLog -like "*scripts: OK*") "A_CustomMissile converts"
    Assert-True ($missileLog -notlike "*takes no arguments*") "and is no longer refused"
    Assert-True ($missileLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ((State-Of $missileLog) -ne $plainState) `
        ("and the projectile changed the world: $(State-Of $missileLog)")

    Write-Host "With a projectile name this engine does not know"
    $missileBad = Join-Path $work "missilebad.dec"

    [System.IO.File]::WriteAllText($missileBad, @"
ACTOR ShooterImp 3001
{
  States
  {
  See:
    TROO C 4 A_CustomMissile("NotAThing", 32, 0, 0)
    Goto See
  }
}
"@)

    $missileBadLog = Run-Game "missilebad" @('-decorate', $missileBad)

    Assert-True ($missileBadLog -like "*NotAThing*") "the unknown projectile is named"
    Assert-True ($missileBadLog -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "With A_Jump by frame count"
    $jumpByCount = Join-Path $work "jumpcount.dec"

    [System.IO.File]::WriteAllText($jumpByCount, @"
ACTOR CountingImp 3001
{
  States
  {
  See:
    TROO A 4 A_Jump(128, 2)
    TROO B 4
    Loop
  }
}
"@)

    $countLog = Run-Game "jumpcount" @('-decorate', $jumpByCount)

    Assert-True ($countLog -like "*frame count*") "a jump by frame count is named"
    Assert-True ($countLog -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "With a truncated file"
    $cutLog = Run-Game "cut" @('-decorate', $truncated)

    Assert-True ($cutLog -like "*map MAP01: OK*") "a cut-short file does not stop the game"
    Assert-True ($cutLog -like "*not closed*") "and the missing brace is reported"

    Write-Host "DECORATE test passed."
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
