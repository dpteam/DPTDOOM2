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
    $name = [System.Text.Encoding]::ASCII.GetBytes($lumpName.PadRight(8).Substring(0, 8))

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

    # A block that also asks for states, which this engine cannot take yet.
    $withStates = Join-Path $work "states.dec"
    @"
ACTOR TestImp 3001
{
  Health 400
  States
  {
  Spawn:
    TROO AB 10 A_Look
    Loop
  }
}
"@ | Set-Content -Path $withStates -Encoding ASCII

    # A doomednum this engine does not have: a new actor, which needs states and a sprite.
    $newActor = Join-Path $work "new.dec"
    @"
ACTOR BrandNew 13579
{
  Health 100
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
    Assert-True ($withStatesLog -like "*decorate problem*") "the part that did not convert is reported"
    Assert-True ($withStatesLog -like "*States section is not converted*") "named as the States section"
    Assert-True ($withStatesLog -like "*states.dec*" -or $withStatesLog -like "*TestImp*") `
        "with the actor it belongs to"
    Assert-True ($withStatesLog -like "*keeps the engine's own states*") `
        "and what happened instead"

    # The numbers in the same file must still have applied: the report is about the states,
    # not about the whole block. Compared against the run with no DECORATE at all, because
    # this file sets fewer properties than numbers.dec and so is legitimately a third world.
    Assert-True ((State-Of $withStatesLog) -ne $plainState) `
        "the numbers around the States section still applied"

    Write-Host "With a new actor"
    $newLog = Run-Game "new" @('-decorate', $newActor)

    Assert-True ($newLog -like "*map MAP01: OK*") "the level still loads"
    Assert-True ($newLog -like "*13579*") "the doomednum is named"
    Assert-True ($newLog -like "*not created*") "and the outcome: the actor is not created"

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
