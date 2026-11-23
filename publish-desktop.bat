@echo off
rem Builds desktop\dist\OpenVP.exe on this PC: the OpenVP Electron app in one file.
rem Needs the .NET 8 SDK and Node.js to build; the exe itself runs without either.
setlocal
cd /d "%~dp0desktop"
if not exist node_modules (
  call npm install --no-audit --no-fund || goto :locked
echo.
echo Can't replace desktop\dist\win-unpacked: something is still using it.
echo Close OpenVP (check the system tray and Task Manager for OpenVP.exe and OpenVP.Server.exe), then run this again.
pause
exit /b 1

:failed
)
rem The last build's unpacked app is replaced by this one. If OpenVP is still running from it, Windows won't let go of its files.
if exist dist\win-unpacked rmdir /s /q dist\win-unpacked 2>nul
if exist dist\win-unpacked goto :locked
call npm run build || goto :locked
echo.
echo Can't replace desktop\dist\win-unpacked: something is still using it.
echo Close OpenVP (check the system tray and Task Manager for OpenVP.exe and OpenVP.Server.exe), then run this again.
pause
exit /b 1

:failed
echo.
echo Done: desktop\dist\OpenVP.exe
exit /b 0

:locked
echo.
echo Can't replace desktop\dist\win-unpacked: something is still using it.
echo Close OpenVP (check the system tray and Task Manager for OpenVP.exe and OpenVP.Server.exe), then run this again.
pause
exit /b 1

:failed
echo.
echo Build failed. The error is above. Building needs the .NET 8 SDK (https://dotnet.microsoft.com/download) and Node.js (https://nodejs.org).
pause
exit /b 1
