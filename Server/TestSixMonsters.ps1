param([string]$ServerExe = (Join-Path $PSScriptRoot 'GameServer.exe'))
$ErrorActionPreference = 'Stop'
if (Get-NetTCPConnection -LocalPort 7777 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Port 7777 is in use; existing server preserved.'
}

function Connect-Peer {
    $client = [Net.Sockets.TcpClient]::new()
    $client.Connect('127.0.0.1', 7777)
    return [pscustomobject]@{ Client = $client; Buffer = ''; Seen = [Collections.Generic.List[string]]::new() }
}
function Send-Packet($peer, [string]$line) {
    $bytes = [Text.Encoding]::ASCII.GetBytes($line + "`n")
    $peer.Client.GetStream().Write($bytes, 0, $bytes.Length)
}
function Read-Line($peer) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ([DateTime]::UtcNow -lt $deadline) {
        $end = $peer.Buffer.IndexOf("`n")
        if ($end -ge 0) {
            $line = $peer.Buffer.Substring(0, $end).Trim()
            $peer.Buffer = $peer.Buffer.Substring($end + 1)
            $peer.Seen.Add($line)
            return $line
        }
        $stream = $peer.Client.GetStream()
        if (!$stream.DataAvailable) { Start-Sleep -Milliseconds 10; continue }
        $bytes = [byte[]]::new(8192)
        $length = $stream.Read($bytes, 0, $bytes.Length)
        if ($length -eq 0) { throw 'Unexpected disconnect' }
        $peer.Buffer += [Text.Encoding]::ASCII.GetString($bytes, 0, $length)
    }
    throw 'Timed out reading server packet'
}
function Wait-Packet($peer, [string]$pattern) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ([DateTime]::UtcNow -lt $deadline) {
        $line = Read-Line $peer
        if ($line -match $pattern) { return $line }
    }
    throw "Timed out waiting for $pattern"
}
function Check-Monsters($peer) {
    $expected = @{ 1='Vision'; 2='Sound'; 3='Vision'; 4='Vision'; 5='Sound'; 6='Vision' }
    $found = @{}
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ($found.Count -lt 6 -and [DateTime]::UtcNow -lt $deadline) {
        $line = Read-Line $peer
        if ($line -notmatch '^S_MONSTER_STATE\|(\d+)\|([^|]+)\|') { continue }
        $id = [int]$Matches[1]
        if ($expected.ContainsKey($id)) {
            if ($Matches[2] -ne $expected[$id]) { throw "Monster $id has wrong type: $line" }
            $found[$id] = $line
        }
    }
    if ($found.Count -ne 6) { throw "Received $($found.Count)/6 monster states" }
    return $found
}

$server = Start-Process -FilePath $ServerExe -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $env:TEMP 'noa-six-monsters-server.log') `
    -RedirectStandardError (Join-Path $env:TEMP 'noa-six-monsters-error.log')
$peers = @()
try {
    Start-Sleep -Milliseconds 700
    for ($id = 1; $id -le 2; $id++) {
        $peer = Connect-Peer
        $peers += $peer
        $null = Wait-Packet $peer '^LOBBY\|'
        Send-Packet $peer "C_ROLE|$($id - 1)"
        Send-Packet $peer 'C_READY|1'
    }
    Start-Sleep -Milliseconds 200
    Send-Packet $peers[0] 'C_START'
    $tokens = @()
    foreach ($peer in $peers) {
        $tokens += (Wait-Packet $peer '^S_SESSION\|').Split('|')[1]
        $seed = (Wait-Packet $peer '^START\|').Split('|')[1]
    }

    $edges = [int[]]::new(25)
    for ($z=0; $z -lt 5; $z++) { for ($x=0; $x -lt 5; $x++) {
        $i=$z*5+$x
        if ($x -lt 4) { $edges[$i]=$edges[$i] -bor 1; $edges[$i+1]=$edges[$i+1] -bor 2 }
        if ($z -lt 4) { $edges[$i]=$edges[$i] -bor 4; $edges[$i+5]=$edges[$i+5] -bor 8 }
    } }
    $mask = -join ($edges | ForEach-Object { $_.ToString('X') })
    $spawns = '0|0|4|8|0|4|4|0|8|4|0|0|8|0|8|0|0|8'
    for ($id=1; $id -le 2; $id++) {
        $peer=$peers[$id-1]
        Send-Packet $peer "C_NAV_BEGIN|$id|$seed|5|5|0|0|2|4|4|1|$spawns"
        for ($item=1; $item -le 3; $item++) { Send-Packet $peer "C_NAV_ITEM|$id|$item|Clue|1|0.85|1" }
        Send-Packet $peer "C_NAV_CHUNK|$id|0|$mask"
        Send-Packet $peer "C_NAV_END|$id"
    }
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_NAV_READY$' }
    $first = Check-Monsters $peers[0]
    $second = Check-Monsters $peers[1]
    for ($id=1; $id -le 6; $id++) {
        if ($first[$id] -ne $second[$id]) { throw "Monster $id state differs between peers" }
    }

    Send-Packet $peers[0] 'MOVE|1|0|0|7|0|0|0|0|1|0'
    $attack1 = Wait-Packet $peers[0] '^S_MONSTER_STATE\|6\|Vision\|Attack\|'
    $attack2 = Wait-Packet $peers[1] '^S_MONSTER_STATE\|6\|Vision\|Attack\|'
    if ($attack1 -ne $attack2) { throw 'Monster 6 attack state differs between peers' }
    $damage1 = Wait-Packet $peers[0] '^S_PLAYER_DAMAGE\|1\|10\|6$'
    $damage2 = Wait-Packet $peers[1] '^S_PLAYER_DAMAGE\|1\|10\|6$'
    if ($damage1 -ne $damage2) { throw 'Monster 6 damage differs between peers' }
    $null = Wait-Packet $peers[0] '^S_PLAYER_HP\|1\|90$'
    $null = Wait-Packet $peers[1] '^S_PLAYER_HP\|1\|90$'

    $peers[1].Client.Close()
    $null = Wait-Packet $peers[0] '^S_CONNECTION\|2\|0$'
    $peers[1] = Connect-Peer
    $null = Wait-Packet $peers[1] '^S_RESUME_REQUIRED$'
    Send-Packet $peers[1] "C_RESUME|$($tokens[1])"
    $null = Wait-Packet $peers[1] '^S_RESUME_ACCEPTED'
    $null = Wait-Packet $peers[1] '^START\|'
    Send-Packet $peers[1] 'C_RESUME_READY'
    $null = Wait-Packet $peers[1] '^S_SNAPSHOT_BEGIN$'
    $restored = Check-Monsters $peers[1]
    $null = Wait-Packet $peers[1] '^S_SNAPSHOT_END$'
    Send-Packet $peers[1] 'C_RESUME_ACK'
    $null = Wait-Packet $peers[1] '^S_RESUME_COMPLETE$'
    Write-Output 'PASS: two peers received matching IDs 1-6, monster 6 attack/damage/HP, and reconnect restored all six states'
} finally {
    foreach ($peer in $peers) { if ($peer -and $peer.Client) { $peer.Client.Close() } }
    if (!$server.HasExited) { Stop-Process -Id $server.Id }
}
