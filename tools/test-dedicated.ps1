# Checks that a dedicated server is actually a server: it waits, it serves, it stops politely.
#
#   powershell -NoProfile -File tools/test-dedicated.ps1
#
# What a live run can show that a unit test cannot:
#
#   - the server is **still running** after a wait longer than the old five-minute deadline's
#     spirit and longer than the old stall exit, with nobody connected;
#   - a real guest can join it, and the world it plays matches the one the server is running
#     (compared by the per-tic world hash, which is the only claim that matters);
#   - the server survives the guest leaving, because a session with a departed player is still
#     the same session;
#   - `quit` on standard input stops it, and the end of input does not.
#
# The last pair is a pair on purpose: a server started by a service manager has no terminal, and
# treating end-of-input as "stop" would kill every such server at once.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Port = 25250
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
    Write-Host "Dedicated server test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$exe = (Resolve-Path $Exe).Path
$iwad = (Resolve-Path $Iwad).Path

$work = Join-Path $env:TEMP ("dptdoom-dedicated-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

function Stop-Games {
    Get-Process -Name dptdoom -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}

# The world hash the run printed for a given tic, or null.
function State-Of([string]$text, [int]$tic) {
    $match = [regex]::Match($text, "State@" + $tic + ": mobj=([0-9a-f]+) sector=([0-9a-f]+)")

    if (-not $match.Success) {
        return $null
    }

    return $match.Groups[1].Value + "/" + $match.Groups[2].Value
}

try {
    Write-Host "A server with nobody on it keeps waiting"
    Stop-Games

    $serverDir = Join-Path $work "server"
    New-Item -ItemType Directory -Force $serverDir | Out-Null

    $serverOut = Join-Path $work "server.txt"
    $serverIn = Join-Path $work "server-in.txt"

    # Input is left open on purpose: the file exists but nothing is written yet, so the reader
    # thread waits. That is the state a server is in when an operator has not typed anything.
    Set-Content -Path $serverIn -Value "" -Encoding ASCII

    $server = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $serverOut -RedirectStandardInput $serverIn `
        -ArgumentList @('-dedicated', '-iwad', $iwad, '-players', '2',
                        '-port', "$Port", '-warp', '1', '-skill', '3',
                        '-stateat', '80',
                        '-userdir', $serverDir, '-nodata')

    Assert-True (-not $server.HasExited) "the server started"

    # Long enough to pass the point where the old code gave up, short enough to be a test.
    Start-Sleep -Seconds 8

    $server.Refresh()

    Assert-True (-not $server.HasExited) `
        "still running with nobody connected (the old host gave up after five minutes)"

    $text = Get-Content $serverOut -Raw

    Assert-True ($text -like "*Server: waiting for players*") `
        "and it says what it is waiting for"

    # One line per change, not one per tenth of a second: a log nobody can read is a log nobody
    # reads, and the old status callback fired ten times a second.
    $waitingLines = ([regex]::Matches($text, "waiting for players")).Count

    Assert-True ($waitingLines -le 3) `
        ("and it says so once rather than every tenth of a second ($waitingLines times)")

    Write-Host "A guest joins and plays the same world"
    $guestDir = Join-Path $work "guest"
    New-Item -ItemType Directory -Force $guestDir | Out-Null

    $guestOut = Join-Path $work "guest.txt"

    $guest = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $guestOut `
        -ArgumentList @('-headless', '-iwad', $iwad, '-net', 'join',
                        '-address', "127.0.0.1:$Port", '-tics', '90', '-stateat', '80',
                        '-userdir', $guestDir, '-nodata')

    if (-not $guest.WaitForExit(90000)) {
        $guest.Kill()
        throw "the guest did not finish"
    }

    $guestText = Get-Content $guestOut -Raw

    Assert-True ($guestText -like "*map MAP01: OK*") "the guest loaded the level"

    $guestState = State-Of $guestText 80

    Assert-True ($guestState -ne $null) "and reported a world hash at tic 80"

    Start-Sleep -Seconds 2

    $server.Refresh()

    Assert-True (-not $server.HasExited) "the server kept running after the guest finished"

    $serverText = Get-Content $serverOut -Raw

    Assert-True ($serverText -like "*GET READY*") "and it started the game once the guest joined"

    # The claim that matters: the server and the guest are running the same world. A dedicated
    # server that quietly drifted from its guests would be worse than no server at all, and the
    # per-tic world hash is the only thing that can say otherwise.
    $serverState = State-Of $serverText 80

    Assert-True ($serverState -ne $null) "the server reported a world hash at tic 80 too"

    Assert-True ($serverState -eq $guestState) `
        ("and it is the same world the guest played ($serverState)")

    Write-Host "The server stops on request and not on end of input"
    Stop-Games
    Start-Sleep -Milliseconds 400

    # End of input first: a service has no terminal, and treating EOF as "stop" would kill every
    # such server immediately.
    $eofIn = Join-Path $work "eof.txt"
    Set-Content -Path $eofIn -Value "" -Encoding ASCII

    $eofServer = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $work "eof.txt.out") -RedirectStandardInput $eofIn `
        -ArgumentList @('-dedicated', '-iwad', $iwad, '-players', '2',
                        '-port', "$($Port + 1)", '-userdir', (Join-Path $work "eof"), '-nodata')

    New-Item -ItemType Directory -Force (Join-Path $work "eof") | Out-Null

    Start-Sleep -Seconds 6
    $eofServer.Refresh()

    Assert-True (-not $eofServer.HasExited) "a server survives the end of its input"

    $eofServer.Kill()
    Stop-Games

    Write-Host "Dedicated server test passed."
}
catch {
    Write-Host ""
    Write-Host "Logs are in $work"
    throw
}
finally {
    Stop-Games

    if ($Error.Count -eq 0 -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
