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
# What is deliberately *not* asserted: that a sound is audible. There is no sound device in a
# headless run, so the check is the opposite one - a file that only names sounds must leave the
# world bit for bit as it was, because a sound is presentation and not simulation.

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

    Write-Host "With frame actions this engine can take"
    $actions = Join-Path $work "actions.dec"

    # A_ChangeFlag, A_SetTics, A_Recoil, A_FaceTarget and A_ClearTarget in one block. What this
    # proves is the whole path: the calls are converted, the script parses, and the flag name
    # resolves against the engine's own flags. A frame whose action was dropped would still load,
    # so the check that matters is the absence of a report line.
    [System.IO.File]::WriteAllText($actions, @"
ACTOR ActiveImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    TROO B 4 A_ChangeFlag("NOPAIN", 1)
    TROO C 4 A_SetTics(6)
    TROO D 4 A_Recoil(4)
    TROO E 4 A_FaceTarget(90)
    TROO F 4 A_ClearTarget
    Goto See
  See:
    TROO G 4 A_Chase
    Goto See
  }
}
"@)

    $actionLog = Run-Game "actions" @('-decorate', $actions)

    Assert-True ($actionLog -like "*scripts: OK*") "the frame actions convert"
    Assert-True ($actionLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ($actionLog -notlike "*no such action*") "none of them is a missing action"
    Assert-True ($actionLog -notlike "*takes no arguments*") "and none is refused for its arguments"
    Assert-True ($actionLog -notlike "*decorate problem*") "a block this engine can take reports nothing"

    Write-Host "With a flag A_ChangeFlag names that does not exist"
    $badFlag = Join-Path $work "badflag.dec"

    [System.IO.File]::WriteAllText($badFlag, @"
ACTOR ConfusedImp 3001
{
  States
  {
  See:
    TROO A 4 A_ChangeFlag("NOSUCHFLAG", 1)
    TROO B 4 A_Chase
    Loop
  }
}
"@)

    $badFlagLog = Run-Game "badflag" @('-decorate', $badFlag)

    Assert-True ($badFlagLog -like "*NOSUCHFLAG*") "the unknown flag is named"
    Assert-True ($badFlagLog -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "With an A_LookEx this engine can take"
    $lookEx = Join-Path $work "lookex.dec"

    # The observable here is the report, not the world hash: a headless run has no input, so no
    # monster ever wakes and a look test cannot show up in the world. What this does prove is the
    # whole path - the call is converted, the script parses, and the see-state name resolves
    # against the actor's own chains. A name that did not resolve would throw while the actor was
    # being defined, and the report would say so.
    [System.IO.File]::WriteAllText($lookEx, @"
ACTOR WatchImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_LookEx(LOF_NOSIGHTCHECK|LOF_FULLVOLSEESOUND, 0, 1024.0, 512, 90, "See")
  See:
    TROO C 4 A_Chase
    Goto See
  }
}
"@)

    $lookExLog = Run-Game "lookex" @('-decorate', $lookEx)

    Assert-True ($lookExLog -like "*scripts: OK*") "A_LookEx converts"
    Assert-True ($lookExLog -notlike "*takes no arguments*") "and is no longer refused"
    Assert-True ($lookExLog -notlike "*decorate problem*") "a call this engine can take reports nothing"
    Assert-True ($lookExLog -like "*map MAP01: OK*") "the level loads"

    Write-Host "With an A_LookEx naming a state that does not exist"
    $lookExBad = Join-Path $work "lookexbad.dec"

    # The state name is resolved when the states are emitted, and a name that leads nowhere must
    # cost the name and not the actor: the imp keeps its own see state and the level loads.
    [System.IO.File]::WriteAllText($lookExBad, @"
ACTOR ConfusedImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_LookEx(0, 0, 0, 0, 0, "NoSuchState")
  See:
    TROO C 4 A_Chase
    Goto See
  }
}
"@)

    $lookExBadLog = Run-Game "lookexbad" @('-decorate', $lookExBad)

    Assert-True ($lookExBadLog -like "*NoSuchState*") "the unknown state is named"
    Assert-True ($lookExBadLog -like "*map MAP01: OK*") "and the level still loads"
    Assert-True ($lookExBadLog -notlike "*scripts: FAILED*") "the actor survives it"

    Write-Host "With an A_Explode this engine can take"
    $explode = Join-Path $work "explode.dec"

    # The damage and the radius are different numbers, and the flags are the third argument. What
    # this proves is the whole path: conversion, script parsing, and the state the blast hangs off.
    [System.IO.File]::WriteAllText($explode, @"
ACTOR GrenadeImp 3001
{
  States
  {
  Death:
    TROO A 4 A_Explode(64, 32, XF_HURTSOURCE)
    TROO B 4 A_Explode(128, 128, 0)
    Loop
  }
}
"@)

    $explodeLog = Run-Game "explode" @('-decorate', $explode)

    Assert-True ($explodeLog -like "*scripts: OK*") "A_Explode with arguments converts"
    Assert-True ($explodeLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ($explodeLog -notlike "*takes no arguments*") "and is not refused"
    Assert-True ($explodeLog -notlike "*decorate problem*") "a block this engine can take reports nothing"

    Write-Host "With an A_Explode flag this engine cannot honour"
    $badExplode = Join-Path $work "badexplode.dec"

    [System.IO.File]::WriteAllText($badExplode, @"
ACTOR RocketImp 3001
{
  States
  {
  Death:
    TROO A 4 A_Explode(64, 64, XF_THRUSTZ|XF_CIRCULAR)
    Loop
  }
}
"@)

    $badExplodeLog = Run-Game "badexplode" @('-decorate', $badExplode)

    Assert-True ($badExplodeLog -like "*no counterpart*") "the unsupported flag is named"
    Assert-True ($badExplodeLog -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "With an A_Chase this engine can take"
    $chase = Join-Path $work "chase.dec"

    # The argument order is what this checks: flags in the third position. A converter that read
    # them from the first position would report a state-label mistake and still load, so the
    # absence of a report line is the observable.
    [System.IO.File]::WriteAllText($chase, @"
ACTOR TurretImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    Goto See
  See:
    TROO B 4 A_Chase(0, 0, CHF_DONTMOVE|CHF_NOPLAYACTIVE)
    TROO C 4 A_Chase(0, 0, CHF_STOPIFBLOCKED)
    Loop
  }
}
"@)

    $chaseLog = Run-Game "chase" @('-decorate', $chase)

    Assert-True ($chaseLog -like "*scripts: OK*") "A_Chase with flags converts"
    Assert-True ($chaseLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ($chaseLog -notlike "*the flags go third*") "the flags were read from the third position"
    Assert-True ($chaseLog -notlike "*takes no arguments*") "and are not refused"
    Assert-True ($chaseLog -notlike "*decorate problem*") "a block this engine can take reports nothing"

    Write-Host "With A_Chase written in the order that looks natural"
    $badChase = Join-Path $work "badchase.dec"

    # A_Chase(CHF_DONTMOVE) is the mistake a modder makes. It must be named as a mistake, not
    # quietly accepted as flags.
    [System.IO.File]::WriteAllText($badChase, @"
ACTOR ConfusedImp 3001
{
  States
  {
  See:
    TROO A 4 A_Chase(CHF_DONTMOVE)
    Loop
  }
}
"@)

    $badChaseLog = Run-Game "badchase" @('-decorate', $badChase)

    Assert-True ($badChaseLog -like "*the flags go third*") "the mistake is explained"
    Assert-True ($badChaseLog -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "With A_SpawnItemEx and an unnumbered actor"
    $spawnEx = Join-Path $work "spawnex.dec"

    # This is the shape used throughout ademo1.wad: a named effect, no DoomEdNum, and a
    # randomised argument. The expression must survive conversion; the custom name must resolve
    # after the actor has been allocated.
    [System.IO.File]::WriteAllText($spawnEx, @"
ACTOR Spark : DoomImp
{
  Health 25
  States
  {
  See:
    TROO A 4 A_SpawnItemEx("Spark", 15 - random(0,255) * 0.1171875, 0, 16, 0, 0, 0, 0, SXF_ABSOLUTEPOSITION | SXF_ABSOLUTEVELOCITY | SXF_NOCHECKPOSITION)
    Loop
  }
}
"@)

    $spawnExLog = Run-Game "spawnex" @('-decorate', $spawnEx)

    Assert-True ($spawnExLog -like "*scripts: OK*") "A_SpawnItemEx converts"
    Assert-True ($spawnExLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ($spawnExLog -notlike "*no such actor 'Spark'*") "the unnumbered actor resolves by name"
    Assert-True ($spawnExLog -notlike "*no such spawn expression*") "the random argument has a live expression"
    Assert-True ($spawnExLog -notlike "*decorate problem*") "a supported SpawnItemEx block reports nothing"

    Write-Host "With simple movement and actor actions"
    $simpleActions = Join-Path $work "simpleactions.dec"

    # These are the forms used in ademo1.wad: angle as an expression, relative replacement of
    # velocity, and a target pointer. They must not be reported as unsupported actions.
    [System.IO.File]::WriteAllText($simpleActions, @"
ACTOR ActiveImp 3001
{
  States
  {
  Melee:
    TROO A 4 A_Stop
    TROO B 4 A_NoBlocking(false)
    TROO C 4 A_SetHealth(10)
    TROO D 4 A_SetAngle(angle + 100)
    TROO E 4 A_ChangeVelocity(8, 0, 2, CVF_RELATIVE | CVF_REPLACE, AAPTR_TARGET)
    TROO F 4 A_ScaleVelocity(0.5)
    Loop
  }
}
"@)

    $simpleLog = Run-Game "simpleactions" @('-decorate', $simpleActions)

    Assert-True ($simpleLog -like "*scripts: OK*") "simple actor actions convert"
    Assert-True ($simpleLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ($simpleLog -notlike "*no such action*") "none is a missing action"
    Assert-True ($simpleLog -notlike "*takes no arguments*") "and none is refused for its arguments"
    Assert-True ($simpleLog -notlike "*no such spawn expression*") "the expressions resolve"
    Assert-True ($simpleLog -notlike "*decorate problem*") "a supported block reports nothing"

    Write-Host "With an unsupported action pointer"
    $badPointer = Join-Path $work "badpointer.dec"

    [System.IO.File]::WriteAllText($badPointer, @"
ACTOR ConfusedImp 3001
{
  States
  {
  Melee:
    TROO A 4 A_SetHealth(10, AAPTR_MASTER)
    Loop
  }
}
"@)

    $badPointerLog = Run-Game "badpointer" @('-decorate', $badPointer)

    Assert-True ($badPointerLog -like "*AAPTR_MASTER*") "the unsupported pointer is named"
    Assert-True ($badPointerLog -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "With A_StartSound and its channel options"
    $startSound = Join-Path $work "startsound.dec"

    [System.IO.File]::WriteAllText($startSound, @"
ACTOR SoundImp 3001
{
  States
  {
  Melee:
    TROO A 4 A_StartSound("enemy/test", CHAN_WEAPON, 0, 0.5, ATTN_NONE)
    TROO B 4 A_StartSound("enemy/test", CHAN_BODY, CHANF_DEFAULT, 1, ATTN_NORM)
    Loop
  }
}
"@)

    $startSoundLog = Run-Game "startsound" @('-decorate', $startSound)

    Assert-True ($startSoundLog -like "*scripts: OK*") "A_StartSound converts"
    Assert-True ($startSoundLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ($startSoundLog -notlike "*no such action*") "A_StartSound is a real action"
    Assert-True ($startSoundLog -notlike "*decorate problem*") "a supported sound block reports nothing"

    Write-Host "With an unsupported A_StartSound policy"
    $loopSound = Join-Path $work "loopsound.dec"

    [System.IO.File]::WriteAllText($loopSound, @"
ACTOR LoopImp 3001
{
  States
  {
  Melee:
    TROO A 4 A_StartSound("enemy/test", CHAN_VOICE, CHANF_LOOP, 1, 1)
    Loop
  }
}
"@)

    $loopSoundLog = Run-Game "loopsound" @('-decorate', $loopSound)

    Assert-True ($loopSoundLog -like "*CHANF_LOOP*") "looping is named rather than promised"
    Assert-True ($loopSoundLog -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "With A_ThrowGrenade in a real state"
    $grenade = Join-Path $work "grenade.dec"

    [System.IO.File]::WriteAllText($grenade, @"
ACTOR GrenadeImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
    TROO B 4 A_ThrowGrenade("DoomImpBall", 0, 8, 3, 0)
  See:
    TROO AB 4 A_Chase
    Loop
  }
}
"@)

    $grenadeLog = Run-Game "grenade" @('-decorate', $grenade)

    Assert-True ($grenadeLog -like "*scripts: OK*") "A_ThrowGrenade converts"
    Assert-True ($grenadeLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ($grenadeLog -notlike "*takes no arguments*") "the action is no longer refused"
    Assert-True ($grenadeLog -like "*level throw*") "the missing actor pitch is named"
    Assert-True ((State-Of $grenadeLog) -ne $plainState) "the thrown grenade changed the world"

    Write-Host "With A_AlertMonsters and no parentheses"
    $alert = Join-Path $work "alert.dec"

    [System.IO.File]::WriteAllText($alert, @"
ACTOR AlertImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
  See:
    TROO AB 4 A_AlertMonsters
    TROO CD 4 A_Chase
    Loop
  }
}
"@)

    $alertLog = Run-Game "alert" @('-decorate', $alert)

    Assert-True ($alertLog -like "*scripts: OK*") "the no-parentheses form is accepted"
    Assert-True ($alertLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ($alertLog -notlike "*no such action 'A_AlertMonsters'*") "A_AlertMonsters is a real action"
    Assert-True ((State-Of $alertLog) -ne $plainState) "the alert state changed the world"

    Write-Host "With the conditional checks A_CheckFloor and A_CheckFlag"
    $checks = Join-Path $work "checks.dec"

    [System.IO.File]::WriteAllText($checks, @"
ACTOR CheckImp 3001
{
  States
  {
  Spawn:
    TROO A 4 A_Look
  See:
    TROO A 4 A_CheckFloor("Hit")
    TROO B 4
    Loop
  Hit:
    TROO C 4 A_CheckFlag("AMBUSH", "See")
    TROO D 4
    Loop
  }
}
"@)

    $checksLog = Run-Game "checks" @('-decorate', $checks)

    Assert-True ($checksLog -like "*scripts: OK*") "the conditional checks convert"
    Assert-True ($checksLog -like "*map MAP01: OK*") "the level loads"
    Assert-True ($checksLog -notlike "*no such action*") "neither is a missing action"
    Assert-True ($checksLog -notlike "*decorate problem*") "a block this engine can take reports nothing"
    Assert-True ((State-Of $checksLog) -ne $plainState) "the checks changed the world"

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
