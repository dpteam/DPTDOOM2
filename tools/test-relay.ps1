# End-to-end test for the master relay: a host with no forwarded port, a guest
# that can only reach the master, and a lockstep session over the tunnel.
#
#   powershell -NoProfile -File tools/test-relay.ps1
#
# This runs the real thing rather than a mock: the real master with --relay, two
# real headless game processes, and the real wire protocol. The assertion is the
# one that matters for a relay - the host and the guest must end up with the same
# world hash, because the relay must be invisible to the simulation.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [string]$MasterExe = ".\DPTDOOM.Master\bin\Release\net40\dptdoom-master.exe",
    [int]$ControlPort = 25150,
    [int]$RelayPort = 25151,
    [int]$GamePort = 5031,
    [int]$Tics = 300,
    [int]$TimeoutSeconds = 120
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

function Wait-ForPattern([string]$path, [string]$pattern, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)

    while ((Get-Date) -lt $deadline) {
        if (Test-Path $path) {
            $line = Get-Content $path -ErrorAction SilentlyContinue |
                Select-String -Pattern $pattern |
                Select-Object -First 1

            if ($line) { return $line.ToString() }
        }

        Start-Sleep -Milliseconds 200
    }

    return $null
}

function Get-StateLine([string]$path) {
    if (-not (Test-Path $path)) { return $null }

    $line = Get-Content $path -ErrorAction SilentlyContinue |
        Select-String -Pattern 'State: mobj=' |
        Select-Object -First 1

    if ($line) { return $line.ToString().Trim() }
    return $null
}

if (-not (Test-Path $Iwad)) {
    Write-Host "Relay test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) { throw "Game executable not found: $Exe. Build it first." }
if (-not (Test-Path $MasterExe)) { throw "Master executable not found: $MasterExe. Build it first." }

$temp = Join-Path $env:TEMP ("dptdoom-relay-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $temp | Out-Null

$masterLog = Join-Path $temp "master.log"
$hostOut = Join-Path $temp "host.txt"
$guestOut = Join-Path $temp "guest.txt"

$master = $null
$hostProcess = $null
$guestProcess = $null

try {
    Write-Host "Master with relay: $ControlPort -> $RelayPort"

    $master = Start-Process -FilePath $MasterExe -PassThru -WindowStyle Hidden -ArgumentList @(
        '--bind', '127.0.0.1',
        '--port', $ControlPort,
        '--relay',
        '--relay-port', $RelayPort,
        '--log', $masterLog)

    $ready = Wait-ForPattern $masterLog 'relay listening' 15
    Assert-True ($null -ne $ready) "the relay listener started: $ready"

    # The host never opens a port that the guest uses: the guest is told the
    # master's address and the public session id, nothing else.
    Write-Host "Host: headless, registered on the master, relayed"

    $hostProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $hostOut `
        -ArgumentList @(
            '-headless', '-iwad', $Iwad,
            '-net', 'host', '-players', '2', '-port', $GamePort,
            '-master', "127.0.0.1:$ControlPort", '-relay',
            '-warp', '1', '-skill', '3', '-tics', $Tics)

    $sessionLine = Wait-ForPattern $hostOut 'SESSION ([0-9a-f]{32})' 30
    Assert-True ($null -ne $sessionLine) "the host published a session id: $sessionLine"

    $sessionId = [regex]::Match($sessionLine, '([0-9a-f]{32})').Groups[1].Value
    Write-Host "  session $sessionId"

    $relayOpen = Wait-ForPattern $hostOut 'relay channel open' 30
    Assert-True ($null -ne $relayOpen) "the host attached its relay channel"

    Write-Host "Guest: headless, through the relay"

    $guestProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $guestOut `
        -ArgumentList @(
            '-headless', '-iwad', $Iwad,
            '-net', 'join',
            '-relay-master', "127.0.0.1:$ControlPort",
            '-relay-session', $sessionId,
            '-tics', $Tics)

    $guestProcess.WaitForExit($TimeoutSeconds * 1000) | Out-Null
    $hostProcess.WaitForExit($TimeoutSeconds * 1000) | Out-Null

    $hostState = Get-StateLine $hostOut
    $guestState = Get-StateLine $guestOut

    Assert-True ($null -ne $hostState) "the host simulated the session: $hostState"
    Assert-True ($null -ne $guestState) "the guest simulated the session: $guestState"
    Assert-True ($hostState -eq $guestState) "host and guest agree on the world hash"

    $paired = Select-String -Path $masterLog -Pattern 'paired'
    Assert-True ($null -ne $paired) "the master paired the two channels"

    $guestVia = Select-String -Path $guestOut -Pattern 'VIA RELAY'
    Assert-True ($null -ne $guestVia) "the guest reports it is on the relay"

    Write-Host "Relay test passed."
}
catch {
    Write-Host ""
    Write-Host "Master log:"
    if (Test-Path $masterLog) { Get-Content $masterLog | Select-Object -Last 40 }
    Write-Host ""
    Write-Host "Host output:"
    if (Test-Path $hostOut) { Get-Content $hostOut | Select-Object -Last 20 }
    Write-Host ""
    Write-Host "Guest output:"
    if (Test-Path $guestOut) { Get-Content $guestOut | Select-Object -Last 20 }
    throw
}
finally {
    foreach ($process in @($guestProcess, $hostProcess, $master)) {
        if ($process -and -not $process.HasExited) {
            try { $process.Kill() } catch { }
        }
    }

    # Keep the directory when something failed, so the log is inspectable.
    if ($Error.Count -eq 0 -and (Test-Path $temp)) {
        Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
