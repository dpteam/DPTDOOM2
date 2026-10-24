# Checks the host's ban list and its connection log, with real processes.
#
#   powershell -NoProfile -File tools/test-host-bans.ps1
#
# The unit tests already pin the parser and the matcher. What can only be checked
# with real sockets is the part in between: that a banned address never gets a slot,
# that a banned nickname never gets one, and that the log on disk actually contains
# who joined and who left.
#
# The log matters because its failure mode is silent. A log with the right format and
# no lines looks perfect; a log that records joins but not reasons answers nothing.
#
# Banning 127.0.0.1 is deliberate: it is where this test's own guest comes from, so
# the ban has to be honoured at the handshake rather than by luck of addressing.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$HostPort = 5271,
    [int]$Tics = 300,
    [int]$TimeoutSeconds = 180
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
    Write-Host "Host ban test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-bans-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null

$bannedAddress = Join-Path $work "bans-address.txt"
$bannedName = Join-Path $work "bans-name.txt"
$openBans = Join-Path $work "bans-none.txt"

$hostOut = Join-Path $work "host.txt"
$guestOut = Join-Path $work "guest.txt"
$refusedOut = Join-Path $work "refused.txt"
$logPath = Join-Path $work "connections.log"

Set-Content -Path $bannedAddress -Value "127.0.0.1/32"
Set-Content -Path $bannedName -Value "name unwanted*"
Set-Content -Path $openBans -Value "203.0.113.0/24   # a network nobody in this test uses"

$hostProcess = $null
$guestProcess = $null
$refusedProcess = $null

try {
    # 1. A banned address is refused, and told why.
    Write-Host "Banned address"

    $hostProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $hostOut `
        -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'host', '-players', '2',
                        '-port', "$HostPort", '-warp', '1', '-skill', '3', '-tics', "$Tics",
                        '-bans', $bannedAddress, '-netlog', $logPath)

    Start-Sleep -Seconds 5

    $refusedProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $refusedOut `
        -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'join',
                        '-address', "127.0.0.1:$HostPort", '-tics', '30')
    $refusedProcess.WaitForExit($TimeoutSeconds * 1000) | Out-Null

    $refusedText = Get-Content $refusedOut -Raw -ErrorAction SilentlyContinue
    Assert-True ($refusedText -like "*REFUSED: BANNED*") "the banned guest is refused with a reason"

    $hostText = Get-Content $hostOut -Raw -ErrorAction SilentlyContinue
    Assert-True ($hostText -like "*banned by rule*") "the host says which rule did it"

    # Nothing was allocated for it: the host is still waiting for its player.
    Assert-True ($hostText -notlike "*joined as player*") "a banned address never gets a slot"

    Stop-Quietly $hostProcess
    $hostProcess = $null
    Start-Sleep -Milliseconds 800

    # 2. A banned nickname is refused even though the address is fine.
    Write-Host "Banned nickname"

    $port = $HostPort + 1
    $hostOut2 = Join-Path $work "host2.txt"
    $refusedOut2 = Join-Path $work "refused2.txt"
    $logPath2 = Join-Path $work "connections2.log"

    $hostProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $hostOut2 `
        -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'host', '-players', '2',
                        '-port', "$port", '-warp', '1', '-skill', '3', '-tics', "$Tics",
                        '-bans', $bannedName, '-netlog', $logPath2)

    Start-Sleep -Seconds 5

    $refusedProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $refusedOut2 `
        -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'join',
                        '-address', "127.0.0.1:$port", '-tics', '30', '-name', 'unwanted_guest')
    $refusedProcess.WaitForExit($TimeoutSeconds * 1000) | Out-Null

    $refusedText2 = Get-Content $refusedOut2 -Raw -ErrorAction SilentlyContinue
    Assert-True ($refusedText2 -like "*REFUSED: BANNED*") "a banned nickname is refused"

    Stop-Quietly $hostProcess
    $hostProcess = $null
    Start-Sleep -Milliseconds 800

    # 3. A guest who is not banned plays, and both the join and the leave are logged.
    Write-Host "Unbanned guest"

    $port = $HostPort + 2
    $hostOut3 = Join-Path $work "host3.txt"
    $guestOut3 = Join-Path $work "guest3.txt"
    $logPath3 = Join-Path $work "connections3.log"

    $hostProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $hostOut3 `
        -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'host', '-players', '2',
                        '-port', "$port", '-warp', '1', '-skill', '3', '-tics', "$Tics",
                        '-bans', $openBans, '-netlog', $logPath3)

    Start-Sleep -Seconds 5

    $guestProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $guestOut3 `
        -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'join',
                        '-address', "127.0.0.1:$port", '-tics', '100', '-stateat', '90')

    Assert-True ($guestProcess.WaitForExit($TimeoutSeconds * 1000)) "the unbanned guest finished"

    # The guest stops sooner than the host on purpose: it then says goodbye and leaves,
    # which is the event the log has to record. Two processes ending at the same tic
    # never see each other go, and a test that ended that way would check nothing.
    Start-Sleep -Seconds 2

    Assert-True ($hostProcess.WaitForExit($TimeoutSeconds * 1000)) "the host finished"

    $guestText = Get-Content $guestOut3 -Raw -ErrorAction SilentlyContinue
    Assert-True ($guestText -like "*CONNECTED*") "an address that is not banned still plays"

    $hostText3 = Get-Content $hostOut3 -Raw -ErrorAction SilentlyContinue
    Assert-True ($hostText3 -like "*left on purpose*") "the host saw the guest leave deliberately"

    $log = Get-Content $logPath3 -Raw -ErrorAction SilentlyContinue
    Assert-True ($log -like "*join name=*") "the log records the join"
    Assert-True ($log -like "*address=127.0.0.1*") "and the address"
    Assert-True ($log -like "*leave name=*") "and the leave"

    # The reason is the point of the leave line: "left" alone cannot be investigated.
    Assert-True ($log -like "*reason=*") "and why the player went away"

    Write-Host "Host ban test passed."
}
catch {
    Write-Host ""
    Write-Host "Host output:"
    foreach ($path in @($hostOut, (Join-Path $work "host2.txt"), (Join-Path $work "host3.txt"))) {
        if (Test-Path $path) { Get-Content $path | Select-Object -Last 15 }
    }
    Write-Host ""
    Write-Host "Guest output:"
    foreach ($path in @($guestOut, $refusedOut, $refusedOut2, (Join-Path $work "guest3.txt"))) {
        if (Test-Path $path) { Get-Content $path | Select-Object -Last 10 }
    }
    Write-Host ""
    Write-Host "Connection logs:"
    foreach ($path in @($logPath, $logPath2, $logPath3)) {
        if (Test-Path $path) { Write-Host "-- $path"; Get-Content $path | Select-Object -Last 10 }
    }
    throw
}
finally {
    foreach ($process in @($refusedProcess, $guestProcess, $hostProcess)) {
        Stop-Quietly $process
    }

    # Kept when something failed, so the logs can be read.
    if ($Error.Count -eq 0 -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
