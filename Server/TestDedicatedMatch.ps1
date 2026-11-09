# Run after BuildServer.ps1. Validates two complete matches on one server process.
$ErrorActionPreference = 'Stop'
if (Get-NetTCPConnection -LocalPort 7777 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 7777 is occupied; existing server preserved.' }
function Send-Packet($peer, $line) {
    $bytes = [Text.Encoding]::ASCII.GetBytes($line + "`n")
    $peer.Client.GetStream().Write($bytes, 0, $bytes.Length)
}
function Wait-Packet($peer, $pattern) {
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    while ([DateTime]::UtcNow -lt $deadline) {
        while ($peer.Buffer.Contains("`n")) {
            $end = $peer.Buffer.IndexOf("`n")
            $line = $peer.Buffer.Substring(0, $end).Trim()
            $peer.Buffer = $peer.Buffer.Substring($end + 1)
            if ($line -match $pattern) { return $line }
        }
        $stream = $peer.Client.GetStream()
        $stream.ReadTimeout = 1000
        if (!$stream.DataAvailable) { Start-Sleep -Milliseconds 10; continue }
        $bytes = [byte[]]::new(8192)
        $length = $stream.Read($bytes, 0, $bytes.Length)
        if ($length -eq 0) { throw 'Unexpected disconnect' }
        $peer.Buffer += [Text.Encoding]::ASCII.GetString($bytes, 0, $length)
    }
    throw "Timed out waiting for $pattern"
}
$server = Start-Process (Join-Path $PSScriptRoot 'GameServer.exe') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'match-check.log') -RedirectStandardError (Join-Path $PSScriptRoot 'match-check-error.log')
$peers = @()
try {
    Start-Sleep -Milliseconds 700
    $edges = [int[]]::new(25)
    for ($z=0; $z -lt 5; $z++) { for ($x=0; $x -lt 5; $x++) {
        $i=$z*5+$x
        if ($x -lt 4) { $edges[$i]=$edges[$i] -bor 1; $edges[$i+1]=$edges[$i+1] -bor 2 }
        if ($z -lt 4) { $edges[$i]=$edges[$i] -bor 4; $edges[$i+5]=$edges[$i+5] -bor 8 }
    } }
    $edges[5]=$edges[5] -band (-bnot 4); $edges[10]=$edges[10] -band (-bnot 8)
    $mask= -join ($edges | ForEach-Object { $_.ToString('X') })
    foreach ($count in @(2,4)) {
        $peers=@()
        for ($id=1; $id -le $count; $id++) {
            $client=[Net.Sockets.TcpClient]::new(); $client.Connect('127.0.0.1',7777)
            $peer=[pscustomobject]@{Client=$client;Buffer=''}; $peers+=$peer
            $null=Wait-Packet $peer '^LOBBY\|'
            Send-Packet $peer "C_ROLE|$($id-1)"
            Send-Packet $peer 'C_READY|1'
        }
        Start-Sleep -Milliseconds 200
        Send-Packet $peers[0] 'C_START'
        foreach($peer in $peers){$start=Wait-Packet $peer '^START\|'}
        $seed=$start.Split('|')[1]
        for($id=1;$id -le $count;$id++) {
            $peer=$peers[$id-1]
            Send-Packet $peer "C_NAV_BEGIN|$id|$seed|5|5|0|0|2|4|4|1|0|0|4|0|0|4"
            Send-Packet $peer "C_NAV_PAD|$id|1|1|0|1"
            Send-Packet $peer "C_NAV_PAD|$id|2|3|0|1"
            for($item=1;$item -le 3;$item++){Send-Packet $peer "C_NAV_ITEM|$id|$item|Clue|1|0.85|1"}
            Send-Packet $peer "C_NAV_CHUNK|$id|0|$mask"
            Send-Packet $peer "C_NAV_END|$id"
        }
        foreach($peer in $peers){$null=Wait-Packet $peer '^S_NAV_READY$'}
        $blue=$count-1; $orange=$count
        Send-Packet $peers[$blue-1] "MOVE|$blue|1|0|1|0|0|0|0|1|0"
        Send-Packet $peers[$orange-1] "MOVE|$orange|3|0|1|0|0|0|0|1|0"
        Send-Packet $peers[$blue-1] "C_PUZZLE_PAD|$blue|1|1"
        Send-Packet $peers[$orange-1] "C_PUZZLE_PAD|$orange|2|1"
        for($round=1;$round -le 3;$round++) {
            $clue=Wait-Packet $peers[$blue-1] "^S_PUZZLE_CLUE\|$round\|"
            $options=Wait-Packet $peers[$orange-1] "^S_PUZZLE_CHOICES\|$round\|"
            $answer=$clue.Split('|')[2]; $values=$options.Split('|')
            $choice=[Array]::IndexOf($values,$answer,2)-2
            if($choice -lt 0){throw 'Answer missing from choices'}
            Send-Packet $peers[$orange-1] "C_PUZZLE_CHOOSE|$orange|$choice"
        }
        foreach($peer in $peers){$null=Wait-Packet $peer '^S_PUZZLE_SOLVED$'}
        # No mimic fires in this test. Each solved match must open the server gate on its own.
        $expectedGateCount = if ($count -eq 2) { 1 } else { 2 }
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            Start-Sleep -Milliseconds 100
            $gateCount = @(Select-String -LiteralPath (Join-Path $PSScriptRoot 'match-check.log') -SimpleMatch '[Navigation] Puzzle gate open').Count
        } while ($gateCount -lt $expectedGateCount -and [DateTime]::UtcNow -lt $deadline)
        if ($gateCount -ne $expectedGateCount) { throw 'Puzzle completion did not open the server gate once per match' }
        Send-Packet $peers[0] 'MOVE|1|1|0|1|0|0|0|0|1|0'
        for($item=1;$item -le 3;$item++){Send-Packet $peers[0] "C_ITEM_PICKUP|1|$item|Clue"}
        foreach($peer in $peers){$null=Wait-Packet $peer '^S_GAME_CLEAR$'}
        for($id=1;$id -le $count;$id++){Send-Packet $peers[$id-1] "C_RESTART|$id"}
        foreach($peer in $peers){$null=Wait-Packet $peer '^S_RESTART$'; $peer.Client.Close()}
        Start-Sleep -Milliseconds 200
        if($server.HasExited){throw 'Server exited after restart'}
        Write-Output "PASS: $count players, navigation, three puzzle rounds, three clues, clear, restart vote"
    }
    Write-Output 'PASS: second complete match on the same server; puzzle and items reset'
} finally {
    foreach($peer in $peers){$peer.Client.Close()}
    if(!$server.HasExited){Stop-Process -Id $server.Id}
}
