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
for ($i = 0; $i -lt $count; $i++) {
    $offset = 4 + $i * 160
    $name = [Text.Encoding]::ASCII.GetString($list.Body, $offset + 24, 64).TrimEnd([char]0)
    $ip = "{0}.{1}.{2}.{3}" -f $list.Body[$offset + 16], $list.Body[$offset + 17], $list.Body[$offset + 18], $list.Body[$offset + 19]
    $gamePort = [BitConverter]::ToUInt16($list.Body, $offset + 24 + 124)
    $map = [Text.Encoding]::ASCII.GetString($list.Body, $offset + 24 + 116, 8).TrimEnd([char]0)
    Write-Host ("  {0}  {1}:{2}  {3}" -f $name, $ip, $gamePort, $map)
    if ($name -eq $SessionName) { $found = $true }
}

if (-not $found) { throw "the session did not appear in the list" }

$heartbeat = Invoke-Master 3 $auth
if ($heartbeat.Status -ne 0) { throw "heartbeat failed with status $($heartbeat.Status)" }
Write-Host "Heartbeat: ok"

$remove = Invoke-Master 4 $auth
if ($remove.Status -ne 0) { throw "remove failed with status $($remove.Status)" }
Write-Host "Removed: ok"

$after = Invoke-Master 1 (New-Object byte[] 0)
$countAfter = [BitConverter]::ToUInt32($after.Body, 0)
if ($countAfter -ne ($count - 1)) { throw "session was not removed (before=$count after=$countAfter)" }
Write-Host "List after removal: $countAfter"

Write-Host "Master self test passed."
