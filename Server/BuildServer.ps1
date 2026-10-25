$ErrorActionPreference = 'Stop'

$serverDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio Installer (vswhere.exe) was not found.'
}
$installation = & $vswhere -latest -products '*' -property installationPath
if (-not $installation) {
    throw 'Visual Studio with C++ build tools was not found.'
}
$msbuild = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
$solution = Join-Path $serverDirectory 'GameServer.sln'
& $msbuild $solution '/p:Configuration=Release' '/p:Platform=x64' '/m' '/v:minimal'
if ($LASTEXITCODE -ne 0) {
    throw "GameServer build failed with exit code $LASTEXITCODE."
}
$builtServer = Join-Path $serverDirectory 'x64\Release\GameServer.exe'
if (-not (Test-Path -LiteralPath $builtServer)) {
    throw "Built server was not found at $builtServer."
}
$packagedServer = Join-Path $serverDirectory 'GameServer.exe'
Copy-Item -LiteralPath $builtServer -Destination $packagedServer -Force
Write-Host "Server ready: $packagedServer"
