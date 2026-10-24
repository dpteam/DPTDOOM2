# Checks that the master's configuration file is honoured, and that it complains
# about values it cannot use.
#
#   powershell -NoProfile -File tools/test-master-config.ps1
#
# Three things that used to be constants or absences are now settings, and each has a
# failure mode that is silent:
#
#   - the rate limit. A master behind one NAT'd office, or a launcher that polls too
#     often, hits the old fixed limit with every user; the operator could only rebuild.
#     If the configured number were ignored, the symptom would be "the master rejects
#     everybody" with no way to tell why.
#
#   - per-request logging. A public master writes an event line per LIST, which is the
#     noisiest thing it does and the least interesting. Turned off, the log must still
#     say what the master is doing and what went wrong - a quiet log that also hides
#     warnings is worse than a noisy one.
#
#   - bad values. A typo in a config file must stop the master at startup with a
#     sentence, not start it with a guess. A master that silently uses 60 when the file
#     says 6o is a master whose limits nobody can reason about.

param(
    [string]$MasterExe = ".\DPTDOOM.Master\bin\Release\net40\dptdoom-master.exe",
    [int]$ControlPort = 25170,
    [int]$TimeoutSeconds = 30
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

function New-Header([int]$Operation, [int]$Length) {
    $header = New-Object byte[] 16
    [Text.Encoding]::ASCII.GetBytes("DPTM").CopyTo($header, 0)
    [BitConverter]::GetBytes([uint16]1).CopyTo($header, 4)
    [BitConverter]::GetBytes([uint16]$Operation).CopyTo($header, 6)
    [BitConverter]::GetBytes([uint32]$Length).CopyTo($header, 8)
    return $header
}

function Invoke-Control([int]$Port, [int]$Operation, [byte[]]$Body) {
    $client = New-Object Net.Sockets.TcpClient
    $client.Connect("127.0.0.1", $Port)
    $stream = $client.GetStream()

    $header = New-Header $Operation $Body.Length
    $stream.Write($header, 0, $header.Length)
    if ($Body.Length -gt 0) { $stream.Write($Body, 0, $Body.Length) }

    $responseHeader = New-Object byte[] 16
    $read = 0
    while ($read -lt 16) {
        $count = $stream.Read($responseHeader, $read, 16 - $read)
        if ($count -le 0) { throw "master closed the connection" }
        $read += $count
    }

    $status = [BitConverter]::ToUInt16($responseHeader, 12)
    $length = [BitConverter]::ToInt32($responseHeader, 8)

    $responseBody = New-Object byte[] $length
    $read = 0
    while ($read -lt $length) {
        $count = $stream.Read($responseBody, $read, $length - $read)
        if ($count -le 0) { throw "master closed the connection" }
        $read += $count
    }

    $client.Close()

    return @{ Status = $status; Body = $responseBody }
}

if (-not (Test-Path $MasterExe)) {
    throw "Master executable not found: $MasterExe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-mastercfg-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

$master = $null

function Start-Master([string]$Body, [int]$Port, [string]$Work) {
    $config = Join-Path $Work "master.cfg"
    $log = Join-Path $Work "master.log"

    # The caller writes "log = __LOG__" and gets the path this function then watches.
    # Writing the path twice - once in the body, once here - is how the "events off"
    # case silently waited on the wrong file and reported a master that never started.
    $Body = $Body.Replace("__LOG__", $log)

    Set-Content -Path $config -Value $Body -Encoding ASCII
    Remove-Item $log -ErrorAction SilentlyContinue

    $process = Start-Process -FilePath $MasterExe -PassThru -WindowStyle Hidden `
        -ArgumentList @('--config', $config)

    return @{ Process = $process; Log = $log; Config = $config }
}

try {
    # ------------------------------------------------------- rate limit
    Write-Host "Rate limit from the config file"

    $master = Start-Master @"
bind = 127.0.0.1
port = $ControlPort
log = __LOG__
rate-limit-requests = 2
rate-limit-window-seconds = 60
"@ $ControlPort $work

    $ready = Wait-ForPattern $master.Log 'listening on' $TimeoutSeconds
    Assert-True ($null -ne $ready) "the master started with the configured limits"

    # An empty LIST is one request; two are allowed, the third must be refused.
    $first = Invoke-Control $ControlPort 1 (New-Object byte[] 0)
    $second = Invoke-Control $ControlPort 1 (New-Object byte[] 0)
    $third = Invoke-Control $ControlPort 1 (New-Object byte[] 0)

    Assert-True ($first.Status -eq 0) "the first request is allowed"
    Assert-True ($second.Status -eq 0) "the second is allowed"
    Assert-True ($third.Status -eq 4) "the third is refused, as the file says ($($third.Status))"

    $log = Get-Content $master.Log -Raw -ErrorAction SilentlyContinue
    Assert-True ($log -like "*rate limit*") "and the log says it was the rate limit"

    Stop-Quietly $master.Process
    $master = $null
    Start-Sleep -Seconds 1

    # ------------------------------------------------------- quiet log
    Write-Host "Per-request logging off"

    $quiet = $ControlPort + 1
    $master = Start-Master @"
bind = 127.0.0.1
port = $quiet
log = __LOG__
log-events = off
"@ $quiet $work

    $ready = Wait-ForPattern $master.Log 'listening on' $TimeoutSeconds
    Assert-True ($null -ne $ready) "the master started with events off"

    Invoke-Control $quiet 1 (New-Object byte[] 0) | Out-Null
    Invoke-Control $quiet 1 (New-Object byte[] 0) | Out-Null

    $text = Get-Content $master.Log -Raw -ErrorAction SilentlyContinue
    Assert-True ($text -notlike "*event*") "no per-request lines were written"
    Assert-True ($text -like "*listening on*") "but the startup line still is"

    Stop-Quietly $master.Process
    $master = $null
    Start-Sleep -Seconds 1

    # ------------------------------------------------------- bad values
    #
    # Startup must fail with a sentence naming the key. A master that starts anyway has
    # silently picked a number, and nobody can reason about its limits afterwards.
    Write-Host "Bad config values"

    $badCases = @(
        @{ Key = 'rate-limit-requests'; Value = 'not-a-number' },
        @{ Key = 'rate-limit-window-seconds'; Value = '0' },
        @{ Key = 'rate-limit-addresses'; Value = '2' },
        @{ Key = 'log-events'; Value = 'maybe' }
    )

    $port = $ControlPort + 2

    foreach ($case in $badCases) {
        $body = "bind = 127.0.0.1`nport = $port`nlog = __LOG__`n" +
                $case.Key + " = " + $case.Value

        $config = Join-Path $work "bad.cfg"
        Set-Content -Path $config -Value ($body.Replace("__LOG__", "$work\bad.log")) -Encoding ASCII

        # A failing console program writes to stderr, and PowerShell turns stderr into a
        # terminating error while $ErrorActionPreference is Stop - so the very failure
        # this check is about would abort the script instead of being examined. Caught
        # rather than worked around, because there is no way to read the message
        # otherwise.
        $output = ""
        $code = 0

        try {
            $output = & $MasterExe --config $config 2>&1 | Out-String
            $code = $LASTEXITCODE
        }
        catch {
            $output = $_.Exception.Message
            $code = 1
        }

        Assert-True ($code -ne 0) "a bad '$($case.Key)' stops the master (exit $code)"
        Assert-True ($output -like "*$($case.Key)*") "and the message names the key: $($output.Trim())"
    }

    Write-Host "Master config test passed."
}
catch {
    Write-Host ""
    if ($master) {
        Write-Host "Master log:"
        if (Test-Path $master.Log) { Get-Content $master.Log | Select-Object -Last 30 }
    }
    throw
}
finally {
    if ($master) { Stop-Quietly $master.Process }

    if ($Error.Count -eq 0 -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
