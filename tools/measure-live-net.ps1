# Measures the real game loop, not a headless run.
#
#   powershell -NoProfile -File tools/measure-live-net.ps1 -DelayMs 50 -JitterMs 25 -LossPercent 1
#
# Why this is separate from test-net-latency.ps1: a headless run simulates as
# fast as the network allows and catches up freely, so it reports the best a
# session could do. A real game is capped at 35 tics per second by FramePacer and
# is not allowed to catch up without limit - a stall costs time that is gone.
# Only the live loop shows what a player actually gets.
#
# The -fpslog file is one line per frame:
#   frame, nominal_ms, interval_ms, render_ms, present_ms, wait_ms, tics, frac
# so the achieved rate is the sum of the tics column over the nominal time span.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$DelayMs = 50,
    [int]$JitterMs = 0,
    [int]$LossPercent = 0,
    [int]$Seconds = 25,
    # Skip the first seconds: the level wipe and the handshake are not play.
    [int]$WarmupSeconds = 8,
    [int]$HostPort = 5084,
    [int]$ProxyPort = 5085
)

$ErrorActionPreference = "Stop"

function Stop-Quietly($process) {
    if ($process -and -not $process.HasExited) {
        try { $process.Kill() } catch { }
    }
}

function Get-TicRate([string]$path, [int]$warmupSeconds) {
    if (-not (Test-Path $path)) { return $null }

    $rows = @()

    foreach ($line in Get-Content $path) {
        $parts = $line.Split("`t")

        if ($parts.Length -lt 7) { continue }

        $nominal = 0.0
        $tics = 0

        # The log always uses a dot for the decimal separator, and this machine's
        # culture may not: parsing "0.000" as Russian returns false.
        if (-not [double]::TryParse($parts[1],
                [System.Globalization.NumberStyles]::Float,
                [System.Globalization.CultureInfo]::InvariantCulture,
                [ref]$nominal)) { continue }

        if (-not [int]::TryParse($parts[6], [ref]$tics)) { continue }

        $rows += [pscustomobject]@{ Ms = $nominal; Tics = $tics }
    }

    if ($rows.Count -lt 2) { return $null }

    $last = $rows[$rows.Count - 1].Ms
    $from = $last - ($Seconds * 1000)

    if ($from -lt $warmupSeconds * 1000) {
        $from = $warmupSeconds * 1000
    }

    $sum = 0
    $first = $null
    $end = $null

    foreach ($row in $rows) {
        if ($row.Ms -lt $from) { continue }
        if ($null -eq $first) { $first = $row.Ms }
        $sum += $row.Tics
        $end = $row.Ms
    }

    if ($null -eq $first -or $end -le $first) { return $null }

    return [pscustomobject]@{
        Tics = $sum
        Seconds = ($end - $first) / 1000.0
        Rate = $sum / (($end - $first) / 1000.0)
    }
}

$hostProcess = $null
$guestProcess = $null
$proxy = $null

$hostLog = Join-Path $env:TEMP "live-host.tsv"
$guestLog = Join-Path $env:TEMP "live-guest.tsv"

try {
    Remove-Item $hostLog, $guestLog -ErrorAction SilentlyContinue

    $hostProcess = Start-Process -FilePath $Exe -PassThru `
        -ArgumentList @('-iwad', $Iwad, '-net', 'host', '-players', '2',
                        '-port', "$HostPort", '-warp', '1', '-skill', '3',
                        '-fpslog', $hostLog)

    Start-Sleep -Seconds 6

    if ($DelayMs -gt 0) {
        $proxy = Start-Process -FilePath "powershell.exe" -PassThru -WindowStyle Hidden `
            -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass',
                            '-File', 'tools\latency-proxy.ps1',
                            '-Listen', "$ProxyPort", '-Target', "127.0.0.1:$HostPort",
                            '-DelayMs', "$DelayMs", '-JitterMs', "$JitterMs",
                            '-LossPercent', "$LossPercent")
        Start-Sleep -Seconds 4
    }

    $address = if ($DelayMs -gt 0) { "127.0.0.1:$ProxyPort" } else { "127.0.0.1:$HostPort" }

    $guestProcess = Start-Process -FilePath $Exe -PassThru `
        -ArgumentList @('-iwad', $Iwad, '-net', 'join', '-address', $address,
                        '-fpslog', $guestLog)

    Start-Sleep -Seconds ($WarmupSeconds + $Seconds + 6)

    $label = if ($DelayMs -gt 0) {
        "{0} ms +- {1} ms one way, {2}% loss" -f $DelayMs, $JitterMs, $LossPercent
    } else {
        "direct, no latency"
    }

    Write-Host ""
    Write-Host "condition: $label"
    Write-Host "game wants: 35.0 tics/s"
    Write-Host ""

    foreach ($pair in @(@{ Path = $guestLog; Name = "guest" }, @{ Path = $hostLog; Name = "host " })) {
        $rate = Get-TicRate $pair.Path $WarmupSeconds

        if ($null -eq $rate) {
            Write-Host ("{0}: no data" -f $pair.Name)
            continue
        }

        $verdict = if ($rate.Rate -ge 34.0) { "full speed" } elseif ($rate.Rate -ge 30.0) { "slight slowdown" } else { "SLOW" }

        Write-Host ("{0}: {1,6:0.0} tics/s over {2:0.0} s ({3})" -f
            $pair.Name, $rate.Rate, $rate.Seconds, $verdict)
    }
}
finally {
    Stop-Quietly $guestProcess
    Stop-Quietly $hostProcess
    Stop-Quietly $proxy
    Start-Sleep -Milliseconds 500
}
