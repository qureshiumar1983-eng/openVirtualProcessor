@echo off
rem Builds desktop\dist\OpenVP.exe on this PC: the OpenVP Electron app in one file.
rem Needs the .NET 8 SDK and Node.js to build; the exe itself runs without either.
rem
rem Version: by default the exe is stamped with the latest GitHub release plus "-local" (e.g. 0.1.108-local), so
rem "Check for updates" treats it as current and only offers a newer release. To choose the version yourself:
rem   publish-desktop.bat 0.2.0
rem desktop\package.json is not changed either way.
setlocal
cd /d "%~dp0desktop"

set "VER=%~1"
if not "%VER%"=="" goto :haveversion
rem Latest release tag from GitHub; package.json's version if GitHub can't be reached.
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "$v=''; try { $v=(Invoke-RestMethod -TimeoutSec 10 -Headers @{'User-Agent'='OpenVP-publish'} 'https://api.github.com/repos/qureshiumar1983-eng/openVirtualProcessor/releases/latest').tag_name } catch { }; if ($v -notmatch '^v?\d+\.\d+\.\d+') { $v=(ConvertFrom-Json (Get-Content package.json -Raw)).version }; ($v -replace '^v','' -replace '-.*$','') + '-local'"`) do set "VER=%%v"
:haveversion
if "%VER%"=="" goto :noversion
if /i "%VER:~0,1%"=="v" set "VER=%VER:~1%"
echo Building OpenVP %VER%
echo.

if not exist node_modules (
  call npm install --no-audit --no-fund || goto :failed
)
rem The last build's unpacked app is replaced by this one. If OpenVP is still running from it, Windows won't let go of its files.
if exist dist\win-unpacked rmdir /s /q dist\win-unpacked 2>nul
if exist dist\win-unpacked goto :locked
call npm run build:server || goto :failed
call npm run dist -- "-c.extraMetadata.version=%VER%" || goto :failed
echo.
echo Done: desktop\dist\OpenVP.exe, version %VER%
exit /b 0

:noversion
echo.
echo Couldn't work out a version. Give one instead, e.g.: publish-desktop.bat 0.2.0
pause
exit /b 1

:locked
echo.
echo Can't replace desktop\dist\win-unpacked: something is still using it.
echo Close OpenVP (check the system tray, and Task Manager for OpenVP.exe and OpenVP.Server.exe), then run this again.
pause
exit /b 1

:failed
echo.
echo Build failed. The error is above. Building needs the .NET 8 SDK (https://dotnet.microsoft.com/download) and Node.js (https://nodejs.org).
pause
exit /b 1
