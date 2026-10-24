# Checks that an allow-list rule can raise the relay's quotas, and only for the
# addresses it names.
#
#   powershell -NoProfile -File tools/test-master-allow.ps1
#
# The relay's limits are policy, not capability: two sessions per address covers a
# household with two machines, three guest tunnels covers a four-player co-op. An
# operator's own test rig, or a family behind one router, should not have to edit the
# server's configuration to get past a guess about strangers - so an address in the
# allow list can carry its own raised numbers.
#
# Two masters are started with the same deliberately small quotas. The first has an
# allow list that does not mention this machine, and must stop at the quotas. The
# second has a rule for 127.0.0.1 and must go further. Running both is the point: a
# test that only checked the lifted numbers would pass just as well if nothing were
# limited at all.

param(
    [string]$MasterExe = ".\DPTDOOM.Master\bin\Release\net40\dptdoom-master.exe",
    [int]$ControlPort = 25160,
    [int]$RelayPort = 25161,
    [int]$TimeoutSeconds = 60
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

# --------------------------------------------------------------- wire helpers

function New-Header([int]$Operation, [int]$Length) {
    $header = New-Object byte[] 16
    [Text.Encoding]::ASCII.GetBytes("DPTM").CopyTo($header, 0)
    [BitConverter]::GetBytes([uint16]1).CopyTo($header, 4)
    [BitConverter]::GetBytes([uint16]$Operation).CopyTo($header, 6)
    [BitConverter]::GetBytes([uint32]$Length).CopyTo($header, 8)
    return $header
}

function Invoke-Control([int]$Operation, [byte[]]$Body) {
    $client = New-Object Net.Sockets.TcpClient
    $client.Connect("127.0.0.1", $ControlPort)
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

function New-Info([string]$Name, [int]$GamePort) {
    $info = New-Object byte[] 132

    $nameBytes = [Text.Encoding]::ASCII.GetBytes($Name)
    [Array]::Copy($nameBytes, 0, $info, 0, [Math]::Min($nameBytes.Length, 63))

    for ($i = 64; $i -lt 104; $i++) { $info[$i] = [byte](($i * 7) % 251) }

    $mapBytes = [Text.Encoding]::ASCII.GetBytes("MAP07")
    [Array]::Copy($mapBytes, 0, $info, 116, 5)

    [BitConverter]::GetBytes([uint16]$GamePort).CopyTo($info, 124)
    $info[126] = 3
    $info[127] = 0
    $info[128] = 2
    return $info
}

function Connect-Relay {
    $client = New-Object Net.Sockets.TcpClient
    $client.NoDelay = $true
    $client.Connect("127.0.0.1", $RelayPort)
    return $client
}

function Write-RelayRequest($Stream, [int]$Operation, [byte[]]$Body) {
    $header = New-Header $Operation $Body.Length
    $Stream.Write($header, 0, $header.Length)
    if ($Body.Length -gt 0) { $Stream.Write($Body, 0, $Body.Length) }
}

function Read-RelayReply($Stream) {
    $header = New-Object byte[] 16
    $read = 0
    while ($read -lt 16) {
        $n = $Stream.Read($header, $read, 16 - $read)
        if ($n -le 0) { throw "relay closed the connection" }
        $read += $n
    }

    $status = [BitConverter]::ToUInt16($header, 12)
    $length = [BitConverter]::ToInt32($header, 8)

    $body = New-Object byte[] $length
    $read = 0
    while ($read -lt $length) {
        $n = $Stream.Read($body, $read, $length - $read)
        if ($n -le 0) { throw "relay closed the connection" }
        $read += $n
    }

    return @{ Status = $status; Body = $body }
}

# --------------------------------------------------------------- scenarios

function Start-Master([string]$Work, [string]$AllowRule, [int]$Port, [int]$Relay) {
    $config = Join-Path $Work "master.cfg"
    $allow = Join-Path $Work "allow.txt"
    $log = Join-Path $Work "master.log"

    Set-Content -Path $allow -Value $AllowRule -Encoding ASCII

    # Small quotas on purpose, so reaching them takes four connections rather than
    # a hundred. The point is the comparison, not the size.
    $ini = @(
        "bind = 127.0.0.1",
        "port = $Port",
        "relay = on",
        "relay-port = $Relay",
        "relay-sessions-per-source = 2",
        "relay-guests-per-session = 2",
        "log = $log",
        "allowlist = $allow"
    )

    Set-Content -Path $config -Value $ini -Encoding ASCII

    $process = Start-Process -FilePath $MasterExe -PassThru -WindowStyle Hidden `
        -ArgumentList @('--config', $config)

    $ready = Wait-ForPattern $log 'relay listening' 20

    if ($null -eq $ready) {
        Stop-Quietly $process
        throw "the master did not start; see $log"
    }

    return @{ Process = $process; Log = $log }
}

# How many relayed sessions from this address the master accepts before refusing.
#
# Registration itself is capped per address by the directory (MaxServersPerIp), which
# is a different limit with a different purpose: that one protects the listing, this
# one protects the relay's bandwidth. So registration stops quietly at the two
# failures instead of throwing - hitting the directory cap is not the thing under
# test, and the count of *relayed* sessions is.
function Count-SessionsAccepted {
    $auths = @()

    for ($i = 1; $i -le 6; $i++) {
        $reply = Invoke-Control 2 (New-Info "allow test $i" (5600 + $i))

        if ($reply.Status -ne 0) {
            break
        }

        $auths += ,([byte[]]$reply.Body[0..39])
    }

    $open = @()

    foreach ($auth in $auths) {
        $channel = Connect-Relay
        $stream = $channel.GetStream()
        Write-RelayRequest $stream 5 $auth
        $reply = Read-RelayReply $stream

        if ($reply.Status -ne 0) {
            try { $channel.Close() } catch { }
            break
        }

        $open += $channel
    }

    foreach ($channel in $open) { try { $channel.Close() } catch { } }

    # Free the registrations again. They are leases with a minute to live, and the
    # next check in this script needs the per-address registration budget back - the
    # directory's cap and the relay's cap are different things, and one test should
    # not spend the other's room.
    foreach ($auth in $auths) {
        try { Invoke-Control 4 $auth | Out-Null } catch { }
    }

    return $open.Count
}

# How many guest tunnels one session accepts.
function Count-GuestsAccepted {
    $reply = Invoke-Control 2 (New-Info "allow guest test" 5700)

    if ($reply.Status -ne 0) {
        throw "register failed with status $($reply.Status)"
    }

    $auth = [byte[]]$reply.Body[0..39]
    $id = New-Object byte[] 16
    [Array]::Copy($auth, 0, $id, 0, 16)

    $hostChannel = Connect-Relay
    $hostStream = $hostChannel.GetStream()
    Write-RelayRequest $hostStream 5 $auth

    if ((Read-RelayReply $hostStream).Status -ne 0) {
        try { $hostChannel.Close() } catch { }
        throw "the host channel was refused"
    }

    $channels = @($hostChannel)
    $paired = 0

    for ($i = 1; $i -le 8; $i++) {
        $guest = Connect-Relay
        $guestStream = $guest.GetStream()
        Write-RelayRequest $guestStream 6 $id
        $guestReply = Read-RelayReply $guestStream

        if ($guestReply.Status -ne 0) {
            try { $guest.Close() } catch { }
            break
        }

        $channels += $guest
        $paired++

        # A real host offers a new waiting channel after admitting a guest, so the
        # next guest has something to pair with. Without this the next attempt fails
        # for "no such session" instead of for the quota, and the count would be
        # measuring the wrong thing.
        $again = Connect-Relay
        $againStream = $again.GetStream()
        Write-RelayRequest $againStream 5 $auth
        $againReply = Read-RelayReply $againStream

        if ($againReply.Status -ne 0) {
            try { $again.Close() } catch { }
            break
        }

        $channels += $again
    }

    foreach ($channel in $channels) { try { $channel.Close() } catch { } }

    try { Invoke-Control 4 $auth | Out-Null } catch { }

    return $paired
}

if (-not (Test-Path $MasterExe)) {
    throw "Master executable not found: $MasterExe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-allow-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

$master = $null

try {
    # ------------------------------------------------------------- baseline
    Write-Host "Without an allow rule"

    $master = Start-Master $work "# this machine is not listed`n10.99.0.0/16" $ControlPort $RelayPort

    $sessions = Count-SessionsAccepted
    Assert-True ($sessions -eq 2) "the configured two sessions per address hold ($sessions)"

    Start-Sleep -Seconds 2

    $guests = Count-GuestsAccepted
    Assert-True ($guests -eq 2) "the configured two guest tunnels hold ($guests)"

    Stop-Quietly $master.Process
    $master = $null
    Start-Sleep -Seconds 2

    # The same master again, now with this address in the allow list. Ports are
    # reused deliberately: nothing else is running and a second pair of ports would
    # only make the output harder to read.
    Write-Host "With an allow rule for this address"

    $master = Start-Master $work `
        "127.0.0.1/32 sessions-per-source 4 guests-per-session 4 links-per-source 16" `
        $ControlPort $RelayPort

    $liftedSessions = Count-SessionsAccepted
    Assert-True ($liftedSessions -eq 4) "the raised four sessions per address are honoured ($liftedSessions)"
    Assert-True ($liftedSessions -gt $sessions) "and the raise is strictly more than the default"

    Start-Sleep -Seconds 2

    $liftedGuests = Count-GuestsAccepted
    Assert-True ($liftedGuests -eq 4) "the raised four guest tunnels are honoured ($liftedGuests)"
    Assert-True ($liftedGuests -gt $guests) "and that raise is more than the default too"

    # The rule that raised the numbers is reported, so an operator can see which line
    # did it instead of guessing.
    $log = Get-Content $master.Log -Raw -ErrorAction SilentlyContinue
    Assert-True ($log -like "*raises relay limits*") "the master reports the rule and what it raised"
    Assert-True ($log -like "*sessions/source=4*") "including the numbers, so a wrong one is visible at startup"

    Stop-Quietly $master.Process
    $master = $null
    Start-Sleep -Seconds 2

    # A number below the server's own must not tighten anything. An allow list is a
    # list of grants; if a smaller number silently reduced the limit, a typo would look
    # like the relay being broken for one address and fine for everybody else - which
    # is close to impossible to diagnose from the outside.
    Write-Host "With an allow rule that asks for less than the default"

    $master = Start-Master $work "127.0.0.1/32 sessions-per-source 1" $ControlPort $RelayPort

    $tightened = Count-SessionsAccepted
    Assert-True ($tightened -eq 2) "a smaller number does not lower the configured limit ($tightened)"

    Write-Host "Master allow-list test passed."
}
catch {
    Write-Host ""
    if ($master) {
        Write-Host "Master log:"
        if (Test-Path $master.Log) { Get-Content $master.Log | Select-Object -Last 40 }
    }
    throw
}
finally {
    if ($master) { Stop-Quietly $master.Process }

    if ($Error.Count -eq 0 -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
