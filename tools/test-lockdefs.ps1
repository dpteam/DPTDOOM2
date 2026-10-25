# Checks LOCKDEFS in a real run: the file is read, and its problems are named.
#
#   powershell -NoProfile -File tools/test-lockdefs.ps1
#
# What a real run can show that the unit tests cannot: that the lump in a PWAD is found without
# a flag, that a clean IWAD stays silent (a report that fires on every load stops being read),
# and that a bad file costs its own lock and not the level.
#
# The door itself is checked by BoomLockedDoorTest against a real MAP01. A live check of the
# refusal would need a map with a generalized locked door in it, and there is none in the IWAD -
# the format only appears in PWADs, which this script would have to build from scratch.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe"
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
    Write-Host "LOCKDEFS test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-locks-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

function New-Pwad([string]$path, [string]$lumpName, [string]$content) {
    $data = [System.Text.Encoding]::ASCII.GetBytes($content)

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

function Run-Game([string]$name, [string[]]$extra) {
    $dir = Join-Path $work $name
    New-Item -ItemType Directory -Force $dir | Out-Null

    $arguments = @('-headless', '-iwad', $Iwad, '-warp', '1', '-skill', '3',
                   '-tics', '5', '-userdir', $dir, '-nodata') + $extra

    $output = & $Exe @arguments 2>&1

    return ($output -join "`n")
}

try {
    Write-Host "Without LOCKDEFS"
    $plain = Run-Game "plain" @()

    Assert-True ($plain -like "*map MAP01: OK*") "the level loaded"
    # Matched as the subsystem line, not as a substring: the temporary directory is called
    # dptdoom-locks-*, but a check for "*lockdefs*" would also match a path if the name ever
    # grew that word. The subsystem line is what the loader actually prints.
    Assert-True ($plain -notlike "*lockdefs: OK*") "and nothing about LOCKDEFS appeared"

    Write-Host "With a LOCKDEFS lump"
    $pwad = Join-Path $work "locks.wad"

    New-Pwad $pwad "LOCKDEFS" @"
// A lock written by hand.
lock 1
{
    message "You need the blue card"
    any { bluecard blueskull }
}

lock 7
{
    lockedsound oof
    anyred
}
"@

    $withLocks = Run-Game "locks" @('-file', $pwad)

    Assert-True ($withLocks -like "*lockdefs: OK (2 lock(s) defined)*") `
        "the lump was found without a flag"
    Assert-True ($withLocks -like "*map MAP01: OK*") "the level still loaded"

    Write-Host "With a file named on the command line"
    $text = Join-Path $work "locks.txt"
    [System.IO.File]::WriteAllText($text, "lock 2 { message ""Nope"" redcard }`n")

    $fromFile = Run-Game "fromfile" @('-lockdefs', $text)

    Assert-True ($fromFile -like "*lockdefs: OK (1 lock(s) defined)*") "the path was read"

    Write-Host "With directives this port cannot honour"
    $bad = Join-Path $work "bad.wad"

    New-Pwad $bad "LOCKDEFS" @"
lock 3
{
    message "Nope"
    remotemessage "Another player needs a key"
    mapcolor 255 0 0
    nosuchkey
    bluecard
}
"@

    $badRun = Run-Game "bad" @('-file', $bad)

    Assert-True ($badRun -like "*remotemessage*") "remotemessage is named"
    Assert-True ($badRun -like "*mapcolor*") "mapcolor is named"
    Assert-True ($badRun -like "*nosuchkey*") "an unknown key is named"
    Assert-True ($badRun -like "*map MAP01: OK*") "and the level still loads"

    Write-Host "With clearlocks"
    $clear = Join-Path $work "clear.wad"

    New-Pwad $clear "LOCKDEFS" "clearlocks`nlock 4 { bluecard }`n"

    $clearRun = Run-Game "clear" @('-file', $clear)

    Assert-True ($clearRun -like "*lockdefs: OK (1 lock(s) defined)*") `
        "clearlocks is accepted and the lock after it is read"
    Assert-True ($clearRun -like "*map MAP01: OK*") "the level still loads"

    Write-Host "LOCKDEFS test passed."
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
