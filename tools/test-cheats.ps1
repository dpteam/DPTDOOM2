# Checks the console's cheat commands.
#
#   powershell -NoProfile -File tools/test-cheats.ps1
#
# Two properties matter, and only one of them is "the cheat works":
#
#   - each command actually changes the world, and the commands are not aliases of
#     one another;
#   - none of them works in a network game. A cheat changes the world; in lockstep the
#     other end never learns about the change, so the session desynchronises. That is
#     the rule the typed codes already follow, and the console has to follow it too -
#     otherwise the prompt is a way in that the keyboard is not.
#
# "-consolecmd" runs one command on the first frame of the level, and the headless run
# echoes the in-game console to stdout, which is what makes this testable at all.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Tics = 90,
    [int]$HostPort = 5311
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

if (-not (Test-Path $Iwad)) {
    Write-Host "Cheat test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$temp = Join-Path $env:TEMP "dptdoom-cheats"
New-Item -ItemType Directory -Force $temp | Out-Null

function Run([string]$name, [string[]]$extra) {
    $bmp = Join-Path $temp "$name.bmp"

    $arguments = @('-headless', '-iwad', $Iwad, '-warp', '1', '-skill', '3',
                   '-tics', "$Tics", '-crosshair', '0', '-shot', $bmp) + $extra

    $output = & $Exe @arguments

    return @{ Bitmap = $bmp; Log = ($output -join "`n") }
}

Write-Host "Each command changes the world"

$plain = Run "plain" @()
$plainHash = (Get-FileHash $plain.Bitmap -Algorithm MD5).Hash

$commands = @(
    @{ Line = 'give all';     Name = 'give-all' },
    @{ Line = 'give weapons'; Name = 'give-weapons' },
    @{ Line = 'give ammo';    Name = 'give-ammo' },
    @{ Line = 'give keys';    Name = 'give-keys' },
    @{ Line = 'give saw';     Name = 'give-saw' },
    @{ Line = 'god';          Name = 'god' },
    @{ Line = 'noclip';       Name = 'noclip' },
    @{ Line = 'killem';       Name = 'killem' },

    # The automap cheat changes the map, not the 3D view, so it has to be checked with
    # the map on screen - "-maponly" forces that without touching the session.
    @{ Line = 'revealmap';    Name = 'revealmap'; Extra = @('-maponly') }
)

$hashes = @{}

foreach ($command in $commands) {
    $extra = @('-consolecmd', $command.Line)

    if ($command.ContainsKey('Extra')) {
        $extra += $command.Extra

        # The baseline for a map-only run has to be a map-only run too, or the
        # comparison is between two different screens.
        if (-not $plainMapHash) {
            $plainMap = Run "plain-map" @('-maponly')
            $script:plainMapHash = (Get-FileHash $plainMap.Bitmap -Algorithm MD5).Hash
        }

        $plainHash = $script:plainMapHash
    }
    else {
        $plainHash = (Get-FileHash $plain.Bitmap -Algorithm MD5).Hash
    }

    $result = Run $command.Name $extra

    Assert-True ($null -ne $result) "the run for '$($command.Line)' produced output"

    $hashes[$command.Name] = (Get-FileHash $result.Bitmap -Algorithm MD5).Hash

    Assert-True ($hashes[$command.Name] -ne $plainHash) `
        "'$($command.Line)' changes the frame"

    # The console has to confirm, because the cheat's own message goes to the message
    # line, which is behind the console while it is open.
    $echo = $result.Log
    Assert-True ($echo.Contains("] " + $command.Line)) `
        "'$($command.Line)' is echoed to the console"
}

Write-Host ""
Write-Host "The commands are not aliases of each other"

$byHash = @{}

foreach ($name in $hashes.Keys) {
    $hash = $hashes[$name]

    if ($byHash.ContainsKey($hash)) {
        Assert-True $false "'$name' produces the same frame as '$($byHash[$hash])'"
    }

    $byHash[$hash] = $name
}

Assert-True ($byHash.Count -eq $hashes.Count) `
    "all $($hashes.Count) commands produce distinct frames"

Write-Host ""
Write-Host "Refusals are explained"

$bad = Run "bad-arg" @('-consolecmd', 'give bananas')
Assert-True ($bad.Log.Contains("Expected all, weapons, ammo, keys or saw")) `
    "an unknown 'give' argument is explained"

$unknown = Run "unknown" @('-consolecmd', 'nosuchcommand')
Assert-True ($unknown.Log.Contains("Unknown command")) "an unknown command is explained"

Write-Host ""
Write-Host "Not in a network game"

$hostLog = Join-Path $temp "net-host.txt"
$guestLog = Join-Path $temp "net-guest.txt"
Remove-Item $hostLog, $guestLog -ErrorAction SilentlyContinue

$hostProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $hostLog `
    -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'host', '-players', '2',
                    '-port', "$HostPort", '-warp', '1', '-skill', '3', '-tics', '200')

Start-Sleep -Seconds 5

$guestProcess = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $guestLog `
    -ArgumentList @('-headless', '-iwad', $Iwad, '-net', 'join',
                    '-address', "127.0.0.1:$HostPort", '-tics', '200',
                    '-consolecmd', 'god')

$guestProcess.WaitForExit(120000) | Out-Null
$hostProcess.WaitForExit(30000) | Out-Null

try {
    $guest = Get-Content $guestLog -Raw

    Assert-True ($guest.Contains("Cheats are disabled in a network game")) `
        "the guest's cheat is refused, with the reason"

    # And the refusal did nothing: the two ends still agree on the world.
    $hostState = (Get-Content $hostLog | Select-String 'State: mobj=' | Select-Object -First 1).Line
    $guestState = (Get-Content $guestLog | Select-String 'State: mobj=' | Select-Object -First 1).Line

    Assert-True ($hostState -eq $guestState) `
        "the worlds still match, so the refused cheat changed nothing ($guestState)"
}
finally {
    foreach ($process in @($guestProcess, $hostProcess)) {
        if ($process -and -not $process.HasExited) {
            try { $process.Kill() } catch { }
        }
    }
}

Get-ChildItem $temp -Filter *.bmp | Remove-Item -Force

Write-Host "Cheat command test passed."
