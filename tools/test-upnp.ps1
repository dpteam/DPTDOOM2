# Checks the UPnP port forwarding against whatever network this machine is on.
#
#   powershell -NoProfile -File tools/test-upnp.ps1
#
# A router cannot be reproduced in a test, so this checks what is checkable anywhere and
# reports the rest honestly:
#
#   - without -upnp nothing about UPnP appears, and nothing is attempted;
#   - with -upnp the host either reports a forwarded address or says plainly that it
#     could not forward;
#   - in both cases the host reaches "HOSTING" quickly.
#
# The last point is the one that matters, and it is what this measures: a port-forwarding
# feature that makes every launch wait ten seconds in a network with no UPnP is worse than
# not having the feature. A unit test cannot see that; a clock can.
#
# The measurement is "time until the HOSTING line", not "time until the process exits".
# A host with no guest never starts the game - it waits at GET READY for one - so waiting
# for the map to load would be waiting for something that only happens after a join.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Port = 5281,
    [int]$MaxHostingSeconds = 25
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
    Write-Host "UPnP test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-upnp-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

$hostProcess = $null

# Runs the host and returns the log once HOSTING has appeared, with the seconds it took.
function Start-Hosting([string]$name, [string[]]$extra) {
    $out = Join-Path $work ($name + ".txt")
    $arguments = @('-headless', '-iwad', $Iwad, '-net', 'host', '-port', "$Port", '-players', '2') + $extra

    $watch = [Diagnostics.Stopwatch]::StartNew()

    $script:hostProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $out -ArgumentList $arguments

    $deadline = (Get-Date).AddSeconds($MaxHostingSeconds)

    while ((Get-Date) -lt $deadline) {
        if (Test-Path $out) {
            $text = Get-Content $out -Raw -ErrorAction SilentlyContinue

            if ($text -like "*HOSTING*") {
                $watch.Stop()

                $result = [pscustomobject]@{
                    Text = $text
                    Seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1)
                }

                Stop-Quietly $script:hostProcess
                return $result
            }
        }

        Start-Sleep -Milliseconds 100
    }

    $watch.Stop()
    Stop-Quietly $script:hostProcess

    return [pscustomobject]@{
        Text = (Get-Content $out -Raw -ErrorAction SilentlyContinue)
        Seconds = -1
    }
}

try {
    # ------------------------------------------------------------- without the flag
    Write-Host "Without -upnp"

    $plain = Start-Hosting "plain" @()

    Assert-True ($plain.Seconds -ge 0) "the host reached HOSTING in $($plain.Seconds) s"
    Assert-True ($plain.Text -notlike "*UPnP*") "and said nothing about UPnP"

    # ------------------------------------------------------------- with the flag
    Write-Host "With -upnp"

    $upnp = Start-Hosting "upnp" @('-upnp')

    Assert-True ($upnp.Seconds -ge 0) "the host reached HOSTING in $($upnp.Seconds) s"
    Assert-True ($upnp.Text -like "*UPnP:*") "the status screen says what happened"

    # Twelve seconds is the deadline in the code; the margin covers process start and
    # WAD loading, which this timer includes. The point is that the attempt is bounded
    # rather than that it is fast.
    Assert-True ($upnp.Seconds -lt $MaxHostingSeconds) `
        "the UPnP attempt did not hold the startup ($($upnp.Seconds) s, limit $MaxHostingSeconds)"

    if ($upnp.Text -like "*UPnP: NOT FORWARDED*") {
        # Not a failure: this machine may have no UPnP gateway, which is the common case
        # in an office, a virtual machine, or behind a provider's own NAT.
        Write-Host "  --   no UPnP gateway here, which is a legitimate outcome"

        Assert-True ($upnp.Text -like "*could not forward port*") "the failure is explained"
        Assert-True ($upnp.Text -like "*by hand*" -or $upnp.Text -like "*router*") `
            "and says what to do instead"
    }
    else {
        Write-Host "  --   a router accepted the mapping"

        $line = ($upnp.Text -split "`n" | Select-String 'friends can reach' | Select-Object -First 1)
        Assert-True ($null -ne $line) "the address a friend should use is printed ($line)"
        Assert-True ($line.ToString() -like "*:$Port*") "with the right port"
    }

    Write-Host "UPnP test passed."
}
catch {
    Write-Host ""
    Write-Host "Logs are in $work"
    throw
}
finally {
    Stop-Quietly $hostProcess

    if ($Error.Count -eq 0 -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
