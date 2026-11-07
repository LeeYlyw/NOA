param([string]$PlatformToolset = '')
$ErrorActionPreference = 'Stop'

$serverDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio Installer (vswhere.exe) was not found.'
}
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) {
    throw 'Visual Studio with C++ build tools was not found.'
}
$msbuild = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
if (-not $PlatformToolset) {
    $toolsets = @(Get-ChildItem (Join-Path $installation 'MSBuild\Microsoft\VC\*\Platforms\x64\PlatformToolsets\v*') -Directory |
        Where-Object { $_.Name -match '^v\d+$' } |
        Sort-Object { [int]$_.Name.Substring(1) } -Descending)
    if ($toolsets.Count -eq 0) { throw 'No installed x64 C++ platform toolset was found.' }
    $PlatformToolset = $toolsets[0].Name
}
Write-Host "Building server with $PlatformToolset"
$project = Join-Path $serverDirectory 'GameServer\GameServer.vcxproj'
$outputDirectory = (Join-Path $serverDirectory 'x64\Release') + '\'
& $msbuild $project ('/p:PlatformToolset=' + $PlatformToolset) '/p:Configuration=Release' '/p:Platform=x64' "/p:OutDir=$outputDirectory" '/m' '/v:minimal'
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
