$ErrorActionPreference='Stop'
if (Get-NetTCPConnection -LocalPort 7777 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 7777 is in use; existing server preserved.' }
$server = Start-Process -FilePath (Join-Path $PSScriptRoot 'GameServer.exe') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'session-check.log') -RedirectStandardError (Join-Path $PSScriptRoot 'session-check-error.log')
function Connect-TestClient { $c=[Net.Sockets.TcpClient]::new(); $c.Connect('127.0.0.1',7777); return $c }
function Send-Packet($c,$packet) { $b=[Text.Encoding]::UTF8.GetBytes($packet+"`n"); $c.GetStream().Write($b,0,$b.Length) }
function Read-Until($c,$pattern) { $s=$c.GetStream(); $s.ReadTimeout=5000; $text=''; $b=[byte[]]::new(4096); while($text -notmatch $pattern) { $n=$s.Read($b,0,$b.Length); if($n -eq 0){throw 'Unexpected disconnect'}; $text += [Text.Encoding]::UTF8.GetString($b,0,$n) }; return $text }
$clients=@()
try {
 Start-Sleep -Seconds 32
 if($server.HasExited){throw 'Server exited while idle'}
 foreach($count in @(2,4,2)) {
  $clients=@()
  for($i=0;$i -lt $count;$i++) { $c=Connect-TestClient; $clients+= $c; $null=Read-Until $c 'LOBBY'; Send-Packet $c "C_ROLE|$i"; Send-Packet $c 'C_READY|1' }
  Start-Sleep -Milliseconds 200
  Send-Packet $clients[0] 'C_START'
  foreach($c in $clients){$null=Read-Until $c "START\|\d+\|$count\|"}
  $extra=Connect-TestClient
  try { $null=Read-Until $extra 'S_RESUME_REQUIRED'; Send-Packet $extra 'C_RESUME|invalid'; $null=Read-Until $extra 'S_RESUME_REJECTED' } finally { $extra.Close() }
  $clients[0].Close()
  $null=Read-Until $clients[1] 'S_CONNECTION\|1\|0'
  foreach($c in $clients){$c.Close()}
  Start-Sleep -Seconds 61
  if($server.HasExited){throw 'Server exited after session'}
  Write-Output "PASS: $count-player session, new-player rejection, retained disconnect, grace expiry, next lobby"
 }
 Write-Output 'PASS: idle >30s and three sessions on same server process'
} finally { foreach($c in $clients){$c.Close()}; if(!$server.HasExited){Stop-Process -Id $server.Id} }
