# Checks that a high-ping session still runs at full speed.
#
#   powershell -NoProfile -File tools/test-net-latency.ps1
#   powershell -NoProfile -File tools/test-net-latency.ps1 -DelayMs 100   # 200 ms RTT
#
# Why this exists
# ---------------
# DPT.DOOM is lockstep: a tic cannot be simulated until the peer's command for
# that tic has arrived, and the peer only produces it when it reaches the same
# tic. That couples the tic rate to the network round trip.
#
# Measured before the driver sent commands ahead, on one machine through
# tools/latency-proxy.ps1 at 50 ms one way (about 100 ms of ping, which is
# Russia to the Baltics):
#
#     direct, no latency :  87 tics/s
#     50 ms one way      :  17.5 tics/s   <- the game wants 35
#
# So the world ran at half speed and everything felt late. It looked like a
# relay problem and was not: the relay moved 700 tics in 3.2 s locally against
# 3.5 s direct, and the whole session used about 1.5 KB/s.
#
# The fix is an input delay buffer sized from the measured round trip. The
# assertion here is deliberately about the resulting rate and not about the
# buffer: if someone removes the lookahead, this test fails.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int[]]$DelayMs = @(0, 50, 100),
    [int]$Tics = 350,
    [int]$HostPort = 5074,
    [int]$ProxyPort = 5075,
    # Real internet paths are not clean delays: the extra spread is what made
    # the first fix feel only slightly better.
    [int]$JitterMs = 0,
    [int]$LossPercent = 0,
    # The game asks for 35 tics/s. The bar is a little below that, because a
    # headless run is unthrottled and the point is only to prove the session is
    # no longer capped by the round trip: the old ceiling at 50 ms was 17.5.
    [double]$MinTicsPerSecond = 30
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

if (-not (Test-Path $Iwad)) {
    Write-Host "Latency test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$failures = 0

foreach ($oneWay in $DelayMs) {
    $hostProcess = $null
    $guestProcess = $null
    $proxy = $null

    try {
        $hostOut = Join-Path $env:TEMP "latency-host.txt"
        $guestOut = Join-Path $env:TEMP "latency-guest.txt"
        Remove-Item $hostOut, $guestOut -ErrorAction SilentlyContinue

        $hostProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $hostOut `
            -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'host', '-players', '2',
                            '-port', "$HostPort", '-warp', '1', '-skill', '3', '-tics', "$Tics")

        Start-Sleep -Seconds 5

        if ($oneWay -gt 0) {
            $proxyArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass',
                           '-File', 'tools\latency-proxy.ps1',
                           '-Listen', "$ProxyPort", '-Target', "127.0.0.1:$HostPort",
                           '-DelayMs', "$oneWay", '-JitterMs', "$JitterMs",
                           '-LossPercent', "$LossPercent")

            $proxy = Start-Process -FilePath "powershell.exe" -PassThru -WindowStyle Hidden `
                -ArgumentList $proxyArgs
            Start-Sleep -Seconds 4
        }

        $address = if ($oneWay -gt 0) { "127.0.0.1:$ProxyPort" } else { "127.0.0.1:$HostPort" }

        $watch = [Diagnostics.Stopwatch]::StartNew()

        $guestProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $guestOut `
            -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'join',
                            '-address', $address, '-tics', "$Tics")

        $guestProcess.WaitForExit(300000) | Out-Null
        $watch.Stop()

        $seconds = $watch.Elapsed.TotalSeconds
        $rate = if ($seconds -gt 0) { $Tics / $seconds } else { 0 }

        Write-Host ("one way {0,3} ms : {1} tics in {2} s = {3} tics/s" -f
            $oneWay, $Tics, [math]::Round($seconds, 1), [math]::Round($rate, 1))

        Assert-True ($rate -ge $MinTicsPerSecond) `
            ("$oneWay ms one way is not capped by the round trip ({0} tics/s, need {1})" -f
                [math]::Round($rate, 1), $MinTicsPerSecond)

        $guestState = (Get-Content $guestOut | Select-String 'State: mobj=' | Select-Object -First 1)
        $hostState = $null

        if ($hostProcess.WaitForExit(20000)) {
            $hostState = (Get-Content $hostOut | Select-String 'State: mobj=' | Select-Object -First 1)
        }

        Assert-True ($null -ne $guestState) "the guest finished the session"

        if ($hostState -and $guestState) {
            Assert-True ($hostState.ToString() -eq $guestState.ToString()) `
                "host and guest agree on the world hash at $oneWay ms one way"
        }
    }
    catch {
        Write-Host "  FAIL $($_.Exception.Message)"
        $failures++
    }
    finally {
        Stop-Quietly $guestProcess
        Stop-Quietly $hostProcess
        Stop-Quietly $proxy
        Start-Sleep -Milliseconds 600
    }
}

if ($failures -gt 0) {
    throw "$failures latency check(s) failed."
}

Write-Host "Network latency test passed."
