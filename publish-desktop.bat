@echo off
rem Builds desktop\dist\OpenVP.exe on this PC: the OpenVP Electron app in one file.
rem Needs the .NET 8 SDK and Node.js to build; the exe itself runs without either.
setlocal
cd /d "%~dp0desktop"
if not exist node_modules (
  call npm install --no-audit --no-fund || goto :failed
)
call npm run build || goto :failed
echo.
echo Done: desktop\dist\OpenVP.exe
exit /b 0

:failed
echo.
echo Build failed. You need the .NET 8 SDK (https://dotnet.microsoft.com/download) and Node.js (https://nodejs.org).
pause
exit /b 1
