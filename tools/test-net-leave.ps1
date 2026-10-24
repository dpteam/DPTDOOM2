# Checks that one player leaving does not end the session for everybody.
#
#   powershell -NoProfile -File tools/test-net-leave.ps1
#
# Three players start a co-op game and one of them is killed outright - no goodbye,
# no FIN, the same thing the operating system sees when a machine loses power or a
# laptop's Wi-Fi drops. The host waits out its grace period, gives up on the slot and
# carries on.
#
# What is checked, and why each part matters:
#
#   - the host and the surviving guest both finish the run, at the tic they asked
#     for. Before this, one player's connection ended the game for the rest;
#   - the world hashes match at a named tic. The departed slot cannot be removed -
#     a world built for three players and a world built for two are different worlds -
#     so it is kept and filled with empty commands. If those commands were invented
#     differently on the two ends the worlds would drift, and this is the only check
#     that would notice;
#   - the surviving guest is told who left. It cannot work that out: the departed
#     player looks exactly like somebody standing still.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$HostPort = 5261,
    [int]$Tics = 800,
    [int]$CompareTic = 700,
    [int]$TimeoutSeconds = 300
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
    Write-Host "Leave test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$hostOut = Join-Path $env:TEMP "leave-host.txt"
$guestOut = Join-Path $env:TEMP "leave-guest.txt"
$doomedOut = Join-Path $env:TEMP "leave-doomed.txt"
Remove-Item $hostOut, $guestOut, $doomedOut -ErrorAction SilentlyContinue

$hostProcess = $null
$guestProcess = $null
$doomedProcess = $null

try {
    $hostProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $hostOut `
        -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'host', '-players', '3',
                        '-port', "$HostPort", '-warp', '1', '-skill', '3',
                        '-tics', "$Tics", '-stateat', "$CompareTic")

    Start-Sleep -Seconds 5

    $guestProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $guestOut `
        -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'join',
                        '-address', "127.0.0.1:$HostPort",
                        '-tics', "$Tics", '-stateat', "$CompareTic")

    Start-Sleep -Seconds 3

    $doomedProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $doomedOut `
        -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'join',
                        '-address', "127.0.0.1:$HostPort",
                        '-tics', "$Tics", '-stateat', "$CompareTic")

    # Let the session actually start before pulling the plug, or the third player
    # never joins and nothing is being tested.
    $started = $false
    $watch = [Diagnostics.Stopwatch]::StartNew()

    while ($watch.Elapsed.TotalSeconds -lt 40 -and -not $started) {
        if (Test-Path $doomedOut) {
            $started = [bool](Get-Content $doomedOut -ErrorAction SilentlyContinue |
                Select-String -SimpleMatch 'map MAP01' -Quiet)
        }

        Start-Sleep -Milliseconds 200
    }

    Assert-True $started "all three players are in the world"

    Stop-Quietly $doomedProcess
    Write-Host "  --   player 3 was killed outright"

    Assert-True ($guestProcess.WaitForExit($TimeoutSeconds * 1000)) `
        "the surviving guest finished the session"
    Assert-True ($hostProcess.WaitForExit($TimeoutSeconds * 1000)) `
        "the host finished the session"

    $hostLog = @(Get-Content $hostOut -ErrorAction SilentlyContinue)
    $guestLog = @(Get-Content $guestOut -ErrorAction SilentlyContinue)

    $hostGaveUp = $hostLog | Select-String -SimpleMatch "is now inert" | Select-Object -First 1
    Assert-True ($null -ne $hostGaveUp) "the host gave up on the slot ($hostGaveUp)"

    $guestTold = $guestLog | Select-String -SimpleMatch "left the game; the session continues" |
        Select-Object -First 1
    Assert-True ($null -ne $guestTold) "the surviving guest was told who left ($guestTold)"

    $notEnded = $hostLog | Select-String -SimpleMatch "The other player left the game" | Select-Object -First 1
    Assert-True ($null -eq $notEnded) "the host did not end the game over it"

    # The departed slot is still simulated - it has to be, or the two worlds would
    # differ - but it is no longer counted as a connected player.
    $summary = $hostLog | Select-String -SimpleMatch "players connected 2" | Select-Object -First 1
    Assert-True ($null -ne $summary) "the host stopped counting the departed player ($summary)"

    $leftCount = $hostLog | Select-String -SimpleMatch "left 1" | Select-Object -First 1
    Assert-True ($null -ne $leftCount) "and reports one abandoned slot"

    $hostState = $hostLog | Select-String 'State@' | Select-Object -First 1
    $guestState = $guestLog | Select-String 'State@' | Select-Object -First 1

    Assert-True ($null -ne $hostState) "the host reached tic $CompareTic ($hostState)"
    Assert-True ($null -ne $guestState) "the guest reached tic $CompareTic ($guestState)"
    Assert-True ($hostState.ToString() -eq $guestState.ToString()) `
        "host and guest agree on the world hash at tic $CompareTic"

    Write-Host "Leave test passed."
}
catch {
    Write-Host ""
    Write-Host "Host output:"
    if (Test-Path $hostOut) { Get-Content $hostOut | Select-Object -Last 25 }
    Write-Host ""
    Write-Host "Guest output:"
    if (Test-Path $guestOut) { Get-Content $guestOut | Select-Object -Last 15 }
    throw
}
finally {
    foreach ($process in @($doomedProcess, $guestProcess, $hostProcess)) {
        Stop-Quietly $process
    }
}
