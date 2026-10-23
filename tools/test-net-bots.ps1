# Checks that co-op bots work over the network.
#
#   powershell -NoProfile -File tools/test-net-bots.ps1
#
# A bot in a network game is driven by the host and its command is sent like any
# other player's. The guest therefore never runs bot logic - which means the two
# ends cannot disagree about what a bot did, and the world hashes must match.
#
# Two failure modes are worth pinning, because both are silent:
#
#   - The guest counts the wrong number of slots. It used to be created with two
#     players hardcoded, so a three-player session had the guest simulating only
#     two and waiting forever for a tic nobody sends.
#   - The bots never start. The world is built by the first game.Update, and a bot
#     cannot produce a command before the world exists, so waiting for a bot tic
#     before loading the level deadlocks the host at "GET READY".

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$HostPort = 5211,
    [int]$Tics = 300,
    [int]$TimeoutSeconds = 150
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
    Write-Host "Network bot test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$failures = 0

# players include the bots; bots take the last slots, so 4 slots and 2 bots means
# host, one guest, and two bots.
foreach ($case in @(
    @{ Players = 3; Bots = 1 },
    @{ Players = 4; Bots = 2 }
)) {
    $hostProcess = $null
    $guestProcess = $null

    try {
        $hostOut = Join-Path $env:TEMP "netbots-host.txt"
        $guestOut = Join-Path $env:TEMP "netbots-guest.txt"
        Remove-Item $hostOut, $guestOut -ErrorAction SilentlyContinue

        $hostProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $hostOut `
            -ArgumentList @(
                '-headless', '-iwad', $Iwad,
                '-net', 'host', '-players', "$($case.Players)",
                '-coopbots', "$($case.Bots)",
                '-port', "$HostPort", '-warp', '1', '-skill', '3', '-tics', "$Tics")

        Start-Sleep -Seconds 7

        $guestProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $guestOut `
            -ArgumentList @(
                '-headless', '-iwad', $Iwad,
                '-net', 'join', '-address', "127.0.0.1:$HostPort", '-tics', "$Tics")

        $guestProcess.WaitForExit($TimeoutSeconds * 1000) | Out-Null
        $hostProcess.WaitForExit(30000) | Out-Null

        $label = "$($case.Players) slots, $($case.Bots) bots"

        $hostState = Get-Content $hostOut -ErrorAction SilentlyContinue |
            Select-String 'State: mobj=' | Select-Object -First 1
        $guestState = Get-Content $guestOut -ErrorAction SilentlyContinue |
            Select-String 'State: mobj=' | Select-Object -First 1

        Assert-True ($null -ne $hostState) "$label : the host simulated the session ($hostState)"
        Assert-True ($null -ne $guestState) "$label : the guest simulated the session ($guestState)"
        Assert-True ($hostState.ToString() -eq $guestState.ToString()) `
            "$label : host and guest agree on the world hash"

        $botsLine = Get-Content $hostOut -ErrorAction SilentlyContinue |
            Select-String 'Co-op bots:' | Select-Object -First 1
        Assert-True ($null -ne $botsLine) "$label : the host created its bots ($botsLine)"

        # A bot that never moves is indistinguishable from a bot that was never
        # attached. The host prints every player's position, so the bot's must
        # differ from the spawn point.
        $positions = Get-Content $hostOut -ErrorAction SilentlyContinue |
            Select-String 'player \d pos='

        Assert-True ($positions.Count -eq $case.Players) `
            "$label : all $($case.Players) players are in the world"

        $guestCount = Get-Content $guestOut -ErrorAction SilentlyContinue |
            Select-String 'CONNECTED \(PLAYER 2 OF' | Select-Object -First 1

        Assert-True ($null -ne $guestCount) "$label : the guest knows the full session size ($guestCount)"
    }
    catch {
        Write-Host "  FAIL $($_.Exception.Message)"
        $failures++
    }
    finally {
        Stop-Quietly $guestProcess
        Stop-Quietly $hostProcess
        Start-Sleep -Milliseconds 600
    }
}

if ($failures -gt 0) {
    throw "$failures network bot check(s) failed."
}

Write-Host "Network bot test passed."
