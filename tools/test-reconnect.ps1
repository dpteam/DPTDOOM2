# Checks that a session survives the link being cut in the middle of a game.
#
#   powershell -NoProfile -File tools/test-reconnect.ps1
#
# The guest joins through the latency proxy, and halfway through the run the proxy
# is killed and started again. That is what a flapping route or a NAT mapping that
# expired looks like to both ends: the sockets simply die, with no goodbye.
#
# The session is lockstep, so both ends stall at the same tic and the only thing
# missing is a few commands. The test asserts three things, and each one fails
# separately:
#
#   - the guest reconnected at all (its log says so);
#   - both sides finished the run instead of quitting with "Network: ...";
#   - the world hashes still match, which is the only real proof that the replayed
#     commands put both ends in the same place. A resume that reconnects and then
#     desynchronises would be worse than not resuming at all.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$HostPort = 5241,
    [int]$ProxyPort = 5242,
    [int]$Tics = 4000,
    [double]$PlaySecondsBeforeCut = 1.5,
    [int]$TimeoutSeconds = 240,

    # The tic both sides print a world hash at. The host reaches its -tics limit and
    # quits while the guest can still be a tic or two behind, so the final State
    # lines may describe different tics and report a divergence that is not one.
    [int]$CompareTic = 3800
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

function Stop-Quietly($process) {
    if ($process -and -not $process.HasExited) {
        try { $process.Kill() } catch { }
    }
}

function Start-Proxy() {
    return Start-Process -FilePath "powershell.exe" -PassThru -WindowStyle Hidden `
        -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass',
                        '-File', 'tools\latency-proxy.ps1',
                        '-Listen', "$ProxyPort", '-Target', "127.0.0.1:$HostPort",
                        '-DelayMs', '0')
}

function Wait-ForLine([string]$path, [string]$pattern, [int]$seconds) {
    $watch = [Diagnostics.Stopwatch]::StartNew()

    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        if (Test-Path $path) {
            $hit = Get-Content $path -ErrorAction SilentlyContinue |
                Select-String -SimpleMatch $pattern -Quiet
            if ($hit) { return $true }
        }

        Start-Sleep -Milliseconds 200
    }

    return $false
}

if (-not (Test-Path $Iwad)) {
    Write-Host "Reconnect test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$hostOut = Join-Path $env:TEMP "reconnect-host.txt"
$guestOut = Join-Path $env:TEMP "reconnect-guest.txt"
Remove-Item $hostOut, $guestOut -ErrorAction SilentlyContinue

$hostProcess = $null
$guestProcess = $null
$proxy = $null

try {
    $hostProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $hostOut `
        -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'host', '-players', '2',
                        '-port', "$HostPort", '-warp', '1', '-skill', '3', '-tics', "$Tics",
                        '-stateat', "$CompareTic")

    Start-Sleep -Seconds 5

    $proxy = Start-Proxy
    Start-Sleep -Seconds 3

    $guestProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $guestOut `
        -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'join',
                        '-address', "127.0.0.1:$ProxyPort", '-tics', "$Tics",
                        '-stateat', "$CompareTic")

    Assert-True (Wait-ForLine $hostOut "joined as player 2" 30) "the guest joined through the proxy"

    # Let the game run, then pull the link out from under it.
    Start-Sleep -Milliseconds ([int]($PlaySecondsBeforeCut * 1000))

    Stop-Quietly $proxy
    $proxy = $null
    Start-Sleep -Seconds 2

    $proxy = Start-Proxy

    $guestProcess.WaitForExit($TimeoutSeconds * 1000) | Out-Null
    Assert-True ($guestProcess.HasExited) "the guest finished the session"

    if (-not $hostProcess.WaitForExit(60000)) {
        Write-Host "  FAIL the host did not finish after the guest"
    }

    $guestLog = @(Get-Content $guestOut -ErrorAction SilentlyContinue)
    $hostLog = @(Get-Content $hostOut -ErrorAction SilentlyContinue)

    $reconnected = $guestLog | Select-String -SimpleMatch "reconnected to the host" | Select-Object -First 1
    Assert-True ($null -ne $reconnected) "the guest reconnected ($reconnected)"

    $hostSawIt = $hostLog | Select-String -SimpleMatch "reconnected as player 2" | Select-Object -First 1
    Assert-True ($null -ne $hostSawIt) "the host took the guest back ($hostSawIt)"

    $gaveUp = $guestLog | Select-String -SimpleMatch "connection to the host was lost" | Select-Object -First 1
    Assert-True ($null -eq $gaveUp) "the guest did not give up"

    $guestState = $guestLog | Select-String 'State@' | Select-Object -First 1
    $hostState = $hostLog | Select-String 'State@' | Select-Object -First 1

    Assert-True ($null -ne $guestState) "the guest simulated to the compare tic ($guestState)"
    Assert-True ($null -ne $hostState) "the host simulated to the compare tic ($hostState)"
    Assert-True ($hostState.ToString() -eq $guestState.ToString()) `
        "host and guest agree on the world hash across the cut"
}
catch {
    Write-Host "  FAIL $($_.Exception.Message)"
    throw
}
finally {
    Stop-Quietly $guestProcess
    Stop-Quietly $hostProcess
    Stop-Quietly $proxy
    Start-Sleep -Milliseconds 600
}

Write-Host "Reconnect test passed."
