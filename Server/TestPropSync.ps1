$ErrorActionPreference = 'Stop'
if (Get-NetTCPConnection -LocalPort 7777 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Port 7777 is occupied; existing server preserved.'
}

function Send-Packet($peer, $line) {
    $bytes = [Text.Encoding]::ASCII.GetBytes($line + "`n")
    $peer.Client.GetStream().Write($bytes, 0, $bytes.Length)
}

function Wait-Packet($peer, $pattern, $seconds = 8) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        while ($peer.Buffer.Contains("`n")) {
            $end = $peer.Buffer.IndexOf("`n")
            $line = $peer.Buffer.Substring(0, $end).Trim()
            $peer.Buffer = $peer.Buffer.Substring($end + 1)
            if ($line -match $pattern) { return $line }
        }
        $stream = $peer.Client.GetStream()
        if (-not $stream.DataAvailable) { Start-Sleep -Milliseconds 10; continue }
        $bytes = [byte[]]::new(8192)
        $length = $stream.Read($bytes, 0, $bytes.Length)
        if ($length -eq 0) { throw 'Unexpected disconnect' }
        $peer.Buffer += [Text.Encoding]::ASCII.GetString($bytes, 0, $length)
    }
    throw "Timed out waiting for $pattern"
}

$server = Start-Process (Join-Path $PSScriptRoot 'GameServer.exe') -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $PSScriptRoot 'prop-check.log') `
    -RedirectStandardError (Join-Path $PSScriptRoot 'prop-check-error.log')
$peers = @()
try {
    Start-Sleep -Milliseconds 500
    $edges = [int[]]::new(25)
    for ($z = 0; $z -lt 5; $z++) { for ($x = 0; $x -lt 5; $x++) {
        $i = $z * 5 + $x
        if ($x -lt 4) { $edges[$i] = $edges[$i] -bor 1; $edges[$i + 1] = $edges[$i + 1] -bor 2 }
        if ($z -lt 4) { $edges[$i] = $edges[$i] -bor 4; $edges[$i + 5] = $edges[$i + 5] -bor 8 }
    } }
    $mask = -join ($edges | ForEach-Object { $_.ToString('X') })
    for ($id = 1; $id -le 2; $id++) {
        $client = [Net.Sockets.TcpClient]::new()
        $client.Connect('127.0.0.1', 7777)
        $peer = [pscustomobject]@{ Client = $client; Buffer = '' }
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
        $start = Wait-Packet $peer '^START\|'
    }
    $seed = $start.Split('|')[1]
    for ($id = 1; $id -le 2; $id++) {
        $peer = $peers[$id - 1]
        Send-Packet $peer "C_NAV_BEGIN|$id|$seed|5|5|0|0|2|4|4|1|0|0|4|4|0|4"
        Send-Packet $peer "C_NAV_PAD|$id|1|1|0|1"
        Send-Packet $peer "C_NAV_PAD|$id|2|3|0|1"
        for ($item = 1; $item -le 3; $item++) { Send-Packet $peer "C_NAV_ITEM|$id|$item|Clue|1|0.85|1" }
        foreach ($reward in @(1000, 2000, 3000, 4000, 5000)) {
            Send-Packet $peer "C_NAV_ITEM|$id|$reward|Heal|1|0|1"
        }
        Send-Packet $peer "C_NAV_PROP|$id|Chair|1|1000|1|0|1"
        Send-Packet $peer "C_NAV_PROP|$id|Cassette|1|2000|1|0|1"
        Send-Packet $peer "C_NAV_PROP|$id|Barrel|1|3000|1|0|1"
        Send-Packet $peer "C_NAV_PROP|$id|Bed|1|4000|1|0|1"
        Send-Packet $peer "C_NAV_PROP|$id|Cabinet|1|5000|1|0|1"
        Send-Packet $peer "C_NAV_CHUNK|$id|0|$mask"
        Send-Packet $peer "C_NAV_END|$id"
    }
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_NAV_READY$' }
    Send-Packet $peers[0] 'MOVE|1|1|0|1|0|0|0|0|1|0'
    Start-Sleep -Milliseconds 200

    Send-Packet $peers[0] 'MOVE|1|8|0|8|0|0|0|0|1|0'
    Send-Packet $peers[0] 'C_PROP_USE|1|Chair|1|1'
    $null = Wait-Packet $peers[0] '^S_PROP_DENIED\|RANGE$'
    $null = Wait-Packet $peers[0] '^S_PROP_STATE\|Chair\|1\|0\|0$'
    Send-Packet $peers[0] 'MOVE|1|1|0|1|0|0|0|0|1|0'

    Send-Packet $peers[0] 'C_PROP_USE|1|Cabinet|1'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Cabinet\|1\|1\|1$' }
    Send-Packet $peers[0] 'C_PROP_USE|1|Cabinet|1'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Cabinet\|1\|0\|0$' }
    Send-Packet $peers[0] 'C_ITEM_PICKUP|1|5000|Heal'
    $null = Wait-Packet $peers[0] '^S_ITEM_PICKUP_DENIED\|5000$'
    Send-Packet $peers[0] 'C_PROP_USE|1|Cabinet|1'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Cabinet\|1\|1\|1$' }
    Send-Packet $peers[0] 'C_ITEM_PICKUP|1|1000|Heal'
    $null = Wait-Packet $peers[0] '^S_ITEM_PICKUP_DENIED\|1000$'
    Send-Packet $peers[0] 'C_PROP_USE|1|Chair|1'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Chair\|1\|1\|1$' }
    Send-Packet $peers[0] 'C_PROP_USE|1|Chair|1|1'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Chair\|1\|1\|1$' }
    Send-Packet $peers[0] 'C_ITEM_PICKUP|1|1000|Heal'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_ITEM_PICKUP\|1000\|1\|Heal\|' }
    Send-Packet $peers[0] 'C_PROP_USE|1|Chair|1'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Chair\|1\|0\|1$' }

    Send-Packet $peers[0] 'C_PROP_USE|1|Cassette|1'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Cassette\|1\|1\|0$' }
    Start-Sleep -Milliseconds 800
    Send-Packet $peers[0] 'C_PROP_USE|1|Cassette|1'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Cassette\|1\|0\|0$' }
    Start-Sleep -Seconds 3
    Send-Packet $peers[0] 'C_ITEM_PICKUP|1|2000|Heal'
    $null = Wait-Packet $peers[0] '^S_ITEM_PICKUP_DENIED\|2000$'
    Send-Packet $peers[0] 'C_PROP_USE|1|Cassette|1'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Cassette\|1\|1\|0$' }
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Cassette\|1\|1\|1$' 6 }
    Send-Packet $peers[0] 'C_ITEM_PICKUP|1|4000|Heal'
    $null = Wait-Packet $peers[0] '^S_ITEM_PICKUP_DENIED\|4000$'
    Send-Packet $peers[0] 'C_PROP_USE|1|Barrel|1'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Barrel\|1\|1\|1$' }
    Send-Packet $peers[1] 'MOVE|2|1|0|1|0|0|0|0|1|0'
    Send-Packet $peers[0] 'C_ITEM_PICKUP|1|3000|Heal'
    Send-Packet $peers[1] 'C_ITEM_PICKUP|2|3000|Heal'
    $firstPickup = Wait-Packet $peers[0] '^S_ITEM_PICKUP\|3000\|[12]\|Heal\|'
    $secondPickup = Wait-Packet $peers[1] '^S_ITEM_PICKUP\|3000\|[12]\|Heal\|'
    if ($firstPickup -ne $secondPickup) { throw 'Pickup ownership differs between peers' }
    $winner = [int]$firstPickup.Split('|')[2]
    $loserIndex = if ($winner -eq 1) { 1 } else { 0 }
    $null = Wait-Packet $peers[$loserIndex] '^S_ITEM_PICKUP_DENIED\|3000$'
    Send-Packet $peers[0] 'C_PROP_USE|1|Bed|1'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_PROP_STATE\|Bed\|1\|1\|1$' }
    Send-Packet $peers[0] 'C_ITEM_PICKUP|1|4000|Heal'
    foreach ($peer in $peers) { $null = Wait-Packet $peer '^S_ITEM_PICKUP\|4000\|1\|Heal\|' }
    $peers[1].Client.Close()
    $null = Wait-Packet $peers[0] '^S_CONNECTION\|2\|0$'
    $client = [Net.Sockets.TcpClient]::new()
    $client.Connect('127.0.0.1', 7777)
    $peers[1] = [pscustomobject]@{ Client = $client; Buffer = '' }
    $null = Wait-Packet $peers[1] '^S_RESUME_REQUIRED$'
    Send-Packet $peers[1] "C_RESUME|$($tokens[1])"
    $null = Wait-Packet $peers[1] '^S_RESUME_ACCEPTED$'
    $null = Wait-Packet $peers[1] '^START\|'
    Send-Packet $peers[1] 'C_RESUME_READY'
    foreach ($pattern in @(
        '^S_PROP_STATE\|Barrel\|1\|1\|1$',
        '^S_PROP_STATE\|Bed\|1\|1\|1$',
        '^S_PROP_STATE\|Cabinet\|1\|1\|1$',
        '^S_PROP_STATE\|Cassette\|1\|1\|1$',
        '^S_PROP_STATE\|Chair\|1\|0\|1$',
        '^S_SNAPSHOT_ITEM\|1000\|1\|Heal$',
        '^S_SNAPSHOT_ITEM\|4000\|1\|Heal$'
    )) {
        if ($pattern.StartsWith('^S_PROP_STATE')) { $pattern = $pattern.TrimEnd('$') + '\|[0-9.]+$' }
        $null = Wait-Packet $peers[1] $pattern
    }
    $null = Wait-Packet $peers[1] '^S_SNAPSHOT_END$'
    Send-Packet $peers[1] 'C_RESUME_ACK'
    $null = Wait-Packet $peers[1] '^S_RESUME_COMPLETE$'
    Write-Output 'PASS: two clients share prop and reward states; reconnect restores them'
} finally {
    foreach ($peer in $peers) { $peer.Client.Close() }
    if (-not $server.HasExited) { Stop-Process -Id $server.Id }
}
