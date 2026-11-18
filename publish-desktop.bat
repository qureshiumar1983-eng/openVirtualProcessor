@echo off
rem Builds desktop\dist\OpenVP.exe: the OpenVP Electron app in one file (runs without installing anything).
rem Building needs the .NET 8 SDK and Node.js; running the exe doesn't.
setlocal
cd /d "%~dp0"
dotnet publish src\OpenVP.Server -c Release -r win-x64 --self-contained true -o desktop\server || goto :failed
pushd desktop
call npm ci --no-audit --no-fund || (popd & goto :failed)
call npm run dist || (popd & goto :failed)
popd
echo.
echo Done: desktop\dist\OpenVP.exe
exit /b 0

:failed
echo.
echo Build failed. You need the .NET 8 SDK (https://dotnet.microsoft.com/download) and Node.js (https://nodejs.org).
pause
exit /b 1
