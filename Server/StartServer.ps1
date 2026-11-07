$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'GameServer.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw 'GameServer.exe is missing. Run Server/BuildServer.ps1 first.'
}
$listener = Get-NetTCPConnection -LocalPort 7777 -State Listen -ErrorAction SilentlyContinue
if ($listener) {
    throw 'TCP 7777 is already in use. An existing server may be running; it was not stopped.'
}
Write-Host 'NOA dedicated server - TCP 7777'
Write-Host 'Client on this PC: 127.0.0.1'
Write-Host 'Other PCs on the same network: use the server PC IPv4 address below.'
Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*' } |
    Select-Object InterfaceAlias, IPAddress | Format-Table -AutoSize
Write-Host 'Keep this terminal open while playing. Ctrl+C stops the server.'
& $executable
if ($LASTEXITCODE -ne 0) { throw "Server exited with code $LASTEXITCODE." }
