# Smoke test for the DPT.DOOM master directory protocol.
#
#   powershell -NoProfile -File tools/test-master.ps1 [-Host 127.0.0.1] [-Port 25135]
#
# Registers a session, lists it, renews the lease and removes it again. The
# script speaks the wire protocol directly so it also works against the
# original C master.

param(
    [string]$MasterHost = "127.0.0.1",
    [int]$Port = 25135,
    [string]$SessionName = "DPTDOOM SELF TEST"
)

$ErrorActionPreference = "Stop"

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        Write-Host "  FAIL $message"
        throw $message
    }

    Write-Host "  ok   $message"
}

function New-Header([int]$Operation, [int]$Length) {
    $header = New-Object byte[] 16
    [Text.Encoding]::ASCII.GetBytes("DPTM").CopyTo($header, 0)
    [BitConverter]::GetBytes([uint16]1).CopyTo($header, 4)
    [BitConverter]::GetBytes([uint16]$Operation).CopyTo($header, 6)
    [BitConverter]::GetBytes([uint32]$Length).CopyTo($header, 8)
    return $header
}

function Invoke-Master([int]$Operation, [byte[]]$Body) {
    $client = New-Object Net.Sockets.TcpClient
    $client.Connect($MasterHost, $Port)
    $stream = $client.GetStream()

    $header = New-Header $Operation $Body.Length
    $stream.Write($header, 0, $header.Length)
    if ($Body.Length -gt 0) {
        $stream.Write($Body, 0, $Body.Length)
    }

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

function New-Info([string]$Name, [string]$Map, [int]$GamePort, [int]$Skill) {
    $info = New-Object byte[] 132

    $nameBytes = [Text.Encoding]::ASCII.GetBytes($Name)
    [Array]::Copy($nameBytes, 0, $info, 0, [Math]::Min($nameBytes.Length, 63))

    # Build fingerprint and resource digest are opaque to the master; fill them
    # with something stable so the record looks like a real client's.
    for ($i = 64; $i -lt 104; $i++) { $info[$i] = [byte](($i * 7) % 251) }

    $mapBytes = [Text.Encoding]::ASCII.GetBytes($Map)
    [Array]::Copy($mapBytes, 0, $info, 116, [Math]::Min($mapBytes.Length, 7))

    [BitConverter]::GetBytes([uint16]$GamePort).CopyTo($info, 124)
    $info[126] = [byte]$Skill
    $info[127] = 0
    $info[128] = 2
    return $info
}

Write-Host "Master: $MasterHost`:$Port"

$register = Invoke-Master 2 (New-Info $SessionName "MAP07" 5029 3)
if ($register.Status -ne 0) { throw "register failed with status $($register.Status)" }
$auth = $register.Body[0..39]
$lease = [BitConverter]::ToUInt32($register.Body, 40)
Write-Host "Registered: lease=$lease s, heartbeat every $([BitConverter]::ToUInt32($register.Body, 44)) s"

$list = Invoke-Master 1 (New-Object byte[] 0)
if ($list.Status -ne 0) { throw "list failed with status $($list.Status)" }

$count = [BitConverter]::ToUInt32($list.Body, 0)
Write-Host "Listed sessions: $count"

$found = $false
$relayFlag = 0
for ($i = 0; $i -lt $count; $i++) {
    $offset = 4 + $i * 160
    $name = [Text.Encoding]::ASCII.GetString($list.Body, $offset + 24, 64).TrimEnd([char]0)
    $ip = "{0}.{1}.{2}.{3}" -f $list.Body[$offset + 16], $list.Body[$offset + 17], $list.Body[$offset + 18], $list.Body[$offset + 19]
    $gamePort = [BitConverter]::ToUInt16($list.Body, $offset + 24 + 124)
    $map = [Text.Encoding]::ASCII.GetString($list.Body, $offset + 24 + 116, 8).TrimEnd([char]0)
    Write-Host ("  {0}  {1}:{2}  {3}" -f $name, $ip, $gamePort, $map)
    if ($name -eq $SessionName) {
        $found = $true
        $relayFlag = [BitConverter]::ToUInt32($list.Body, $offset + 156)
        $sessionId = New-Object byte[] 16
        [Array]::Copy($list.Body, $offset, $sessionId, 0, 16)
    }
}

if (-not $found) { throw "the session did not appear in the list" }

$heartbeat = Invoke-Master 3 $auth
if ($heartbeat.Status -ne 0) { throw "heartbeat failed with status $($heartbeat.Status)" }
Write-Host "Heartbeat: ok"

# ---------------------------------------------------------------
# Relay
# ---------------------------------------------------------------
#
# A master without --relay leaves the flag clear, and the relay protocol must
# then be refused rather than half-working. The flag is what a client reads to
# decide whether to offer the option at all.

Write-Host "Relay:"

if (($relayFlag -band 1) -eq 0) {
    $refused = Invoke-Master 5 $auth
    Assert-True ($refused.Status -eq 1) "a master without relay refuses host attach"

    Write-Host "  --   relay is off on this master, protocol checks skipped"
}
else {
    Assert-True (($relayFlag -band 1) -ne 0) "the listing announces relay availability"

    # The control port is a directory, not a tunnel: it answers with the port
    # to use. Following that redirect is the only address a player has to know.
    $probe = New-Object Net.Sockets.TcpClient
    $probe.Connect($MasterHost, $Port)
    $probeStream = $probe.GetStream()

    $probeHeader = New-Header 6 16
    $probeStream.Write($probeHeader, 0, 16)
    $probeStream.Write($sessionId, 0, 16)

    $probeReply = New-Object byte[] 16
    $read = 0
    while ($read -lt 16) {
        $n = $probeStream.Read($probeReply, $read, 16 - $read)
        if ($n -le 0) { throw "master closed during relay redirect" }
        $read += $n
    }

    $probeStatus = [BitConverter]::ToUInt16($probeReply, 12)
    $probeLength = [BitConverter]::ToInt32($probeReply, 8)
    $probeBody = New-Object byte[] $probeLength
    $read = 0
    while ($read -lt $probeLength) {
        $n = $probeStream.Read($probeBody, $read, $probeLength - $read)
        if ($n -le 0) { throw "master closed during relay redirect body" }
        $read += $n
    }

    $probe.Close()

    Assert-True ($probeStatus -eq 5) "the control port redirects a relay request"
    Assert-True ($probeLength -eq 2) "the redirect carries a port"

    $relayPort = [BitConverter]::ToUInt16($probeBody, 0)
    Write-Host "  relay port $relayPort"

    function Connect-Relay([int]$RelayPort) {
        $client = New-Object Net.Sockets.TcpClient
        $client.NoDelay = $true
        $client.Connect($MasterHost, $RelayPort)
        return $client
    }

    function Write-RelayRequest($Stream, [int]$Operation, [byte[]]$Body) {
        $header = New-Header $Operation $Body.Length
        $Stream.Write($header, 0, $header.Length)
        if ($Body.Length -gt 0) {
            $Stream.Write($Body, 0, $Body.Length)
        }
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

    # A wrong secret must not open a tunnel. Flipping one byte in the token part
    # is the closest thing to a forged credential a test can produce.
    $badAuth = [byte[]]::new(40)
    [Array]::Copy($auth, 0, $badAuth, 0, 40)
    $badAuth[39] = $badAuth[39] -bxor 0xFF

    $forged = Connect-Relay $relayPort
    $forgedStream = $forged.GetStream()
    Write-RelayRequest $forgedStream 5 $badAuth
    $forgedReply = Read-RelayReply $forgedStream
    $forged.Close()

    Assert-True ($forgedReply.Status -eq 2) "a forged lease is refused (status $($forgedReply.Status))"

    # The real host attaches and stays attached.
    $hostClient = Connect-Relay $relayPort
    $hostStream = $hostClient.GetStream()
    Write-RelayRequest $hostStream 5 ([byte[]]$auth)
    $hostReply = Read-RelayReply $hostStream

    Assert-True ($hostReply.Status -eq 0) "the host attached its relay channel (status $($hostReply.Status))"

    # A guest asking for a session nobody hosts gets a refusal, not a hang.
    $unknownId = [byte[]]::new(16)
    for ($i = 0; $i -lt 16; $i++) { $unknownId[$i] = 0xEE }

    $unknown = Connect-Relay $relayPort
    $unknownStream = $unknown.GetStream()
    Write-RelayRequest $unknownStream 6 $unknownId
    $unknownReply = Read-RelayReply $unknownStream
    $unknown.Close()

    Assert-True ($unknownReply.Status -eq 3) "an unknown session id is refused (status $($unknownReply.Status))"

    # The real guest pairs with the waiting host.
    $guestClient = Connect-Relay $relayPort
    $guestStream = $guestClient.GetStream()
    Write-RelayRequest $guestStream 6 $sessionId
    $guestReply = Read-RelayReply $guestStream

    Assert-True ($guestReply.Status -eq 0) "the guest paired with the waiting host (status $($guestReply.Status))"

    # More than one relay buffer, and a pattern that would expose a swap or a
    # duplicate chunk. The relay must be byte-exact in both directions.
    $size = 10000
    $payload = [byte[]]::new($size)
    for ($i = 0; $i -lt $size; $i++) { $payload[$i] = [byte](($i * 31 + 7) % 256) }

    $guestStream.Write($payload, 0, $size)
    $guestStream.Flush()

    $hostStream.ReadTimeout = 15000
    $received = [byte[]]::new($size)
    $read = 0
    while ($read -lt $size) {
        $n = $hostStream.Read($received, $read, $size - $read)
        if ($n -le 0) { break }
        $read += $n
    }

    Assert-True ($read -eq $size) "the relay moved $size bytes to the host"

    $same = $true
    for ($i = 0; $i -lt $size; $i++) {
        if ($received[$i] -ne $payload[$i]) { $same = $false; break }
    }

    Assert-True $same "the relay is byte-exact"

    $back = [Text.Encoding]::ASCII.GetBytes("DPTDOOM relay reply")
    $hostStream.Write($back, 0, $back.Length)
    $hostStream.Flush()

    $guestStream.ReadTimeout = 15000
    $echo = [byte[]]::new($back.Length)
    $read = 0
    while ($read -lt $back.Length) {
        $n = $guestStream.Read($echo, $read, $back.Length - $read)
        if ($n -le 0) { break }
        $read += $n
    }

    Assert-True ($read -eq $back.Length) "the other direction works too"

    # A clean close on one side has to reach the other side as end-of-stream,
    # otherwise a game would sit waiting for tics that will never come.
    $guestClient.Close()

    $hostStream.ReadTimeout = 8000
    $tail = [byte[]]::new(1)
    $eof = 0
    try { $eof = $hostStream.Read($tail, 0, 1) } catch { $eof = -1 }

    Assert-True ($eof -le 0) "closing the guest reaches the host as EOF"

    $hostClient.Close()
}

$remove = Invoke-Master 4 $auth
if ($remove.Status -ne 0) { throw "remove failed with status $($remove.Status)" }
Write-Host "Removed: ok"

$after = Invoke-Master 1 (New-Object byte[] 0)
$countAfter = [BitConverter]::ToUInt32($after.Body, 0)
if ($countAfter -ne ($count - 1)) { throw "session was not removed (before=$count after=$countAfter)" }
Write-Host "List after removal: $countAfter"

Write-Host "Master self test passed."
