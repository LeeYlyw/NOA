@echo off
setlocal
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo Visual Studio C++ build tools were not found.
  exit /b 1
)

for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%I"
if not defined VSINSTALL (
  echo Visual Studio C++ build tools were not found.
  exit /b 1
)

call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1

if not exist "%~dp0GameServer\x64\Debug" mkdir "%~dp0GameServer\x64\Debug"
cl /nologo /std:c++17 /EHsc /Fo:"%~dp0..\Temp\GameServer.obj" /Fe:"%~dp0GameServer\x64\Debug\GameServer.exe" "%~dp0GameServer\GameServer.cpp" ws2_32.lib
exit /b %errorlevel%
