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
