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
    [double]$MinTicsPerSecond = 30,

    # How many extra tics the host is given.
    #
    # The host used to be started with the same -tics as the guest, and both run
    # unthrottled: the host reaches the limit first and its process exits while the
    # guest is still a few tics behind. The guest then has nobody to talk to, spends
    # its whole reconnect grace period failing to redial a dead port, and its measured
    # rate collapses to a third of the real one - a failure that has nothing to do with
    # latency, and that got more likely as the input buffer learned to grow.
    #
    # The host would still quit early on "the other player left the game" if it were
    # the guest who finished first, so it needs a margin, not an infinite run.
    [int]$HostLingerTics = 120,

    # The tic both sides print a world hash at.
    #
    # Comparing the final State lines compares different tics: the host reaches its
    # -tics limit and quits while the guest is still a tic or two behind, so the
    # guest stops early on "the other player left the game". The deeper the input
    # buffer the wider the gap - which made this the one check that started failing
    # once the buffer learned to grow, on a session that was perfectly in step.
    [int]$CompareTic = 300
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

# Every case is a link, not just a delay. A clean delay and the same delay with
# jitter are different problems for the input buffer, and the buffer is sized from
# both numbers - so the jittery link gets its own case even when the caller asked
# for none.
$cases = @()

foreach ($oneWay in $DelayMs) {
    $cases += [pscustomobject]@{ OneWayMs = $oneWay; JitterMs = $JitterMs }
}

if ($JitterMs -eq 0) {
    $cases += [pscustomobject]@{ OneWayMs = 50; JitterMs = 30 }
}

for ($case = 0; $case -lt $cases.Count; $case++) {
    $oneWay = $cases[$case].OneWayMs
    $caseJitter = $cases[$case].JitterMs

    # A port per delay, not one port for all of them.
    #
    # Reusing one port across iterations failed intermittently at 100 ms: the
    # previous host was still shutting down, the new one could not bind, and the
    # guest silently connected to the *old* host - which had already simulated its
    # tics. The run then reported a world-hash mismatch that had nothing to do
    # with latency.
    $hostPort = $HostPort + $case * 2
    $proxyPort = $ProxyPort + $case * 2

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
                            '-port', "$hostPort", '-warp', '1', '-skill', '3', '-tics', "$($Tics + $HostLingerTics)",
                            '-stateat', "$CompareTic")

        Start-Sleep -Seconds 5

        if ($oneWay -gt 0) {
            $proxyArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass',
                           '-File', 'tools\latency-proxy.ps1',
                           '-Listen', "$proxyPort", '-Target', "127.0.0.1:$hostPort",
                           '-DelayMs', "$oneWay", '-JitterMs', "$caseJitter",
                           '-LossPercent', "$LossPercent")

            $proxy = Start-Process -FilePath "powershell.exe" -PassThru -WindowStyle Hidden `
                -ArgumentList $proxyArgs
            Start-Sleep -Seconds 4
        }

        $address = if ($oneWay -gt 0) { "127.0.0.1:$proxyPort" } else { "127.0.0.1:$hostPort" }

        $watch = [Diagnostics.Stopwatch]::StartNew()

        $guestProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $guestOut `
            -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'join',
                            '-address', $address, '-tics', "$Tics",
                            '-stateat', "$CompareTic")

        $guestProcess.WaitForExit(300000) | Out-Null
        $watch.Stop()

        $seconds = $watch.Elapsed.TotalSeconds
        $rate = if ($seconds -gt 0) { $Tics / $seconds } else { 0 }

        Write-Host ("one way {0,3} ms, jitter {1,3} ms : {2} tics in {3} s = {4} tics/s" -f
            $oneWay, $caseJitter, $Tics, [math]::Round($seconds, 1), [math]::Round($rate, 1))

        Assert-True ($rate -ge $MinTicsPerSecond) `
            ("$oneWay ms one way with $caseJitter ms of jitter is not capped by the round trip ({0} tics/s, need {1})" -f
                [math]::Round($rate, 1), $MinTicsPerSecond)

        $guestState = (Get-Content $guestOut | Select-String 'State@' | Select-Object -First 1)
        $hostState = $null

        # The host needs its own budget: at 100 ms of latency it finishes the
        # remaining tics after the guest, and 20 s was not always enough. When it
        # ran out the host state was silently absent, so the comparison below was
        # skipped and the run looked like a mismatch instead of a timeout.
        if ($hostProcess.WaitForExit(60000)) {
            $hostState = (Get-Content $hostOut | Select-String 'State@' | Select-Object -First 1)
        }
        else {
            Write-Host "  --   the host did not finish in 60 s; its state is not comparable"
        }

        Assert-True ($null -ne $guestState) "the guest reached tic $CompareTic"

        if ($hostState -and $guestState) {
            Assert-True ($hostState.ToString() -eq $guestState.ToString()) `
                "host and guest agree on the world hash at tic $CompareTic"
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
