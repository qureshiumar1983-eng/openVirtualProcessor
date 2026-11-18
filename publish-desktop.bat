@echo off
rem Builds the OpenVP desktop app into dist\OpenVP (OpenVP.exe + server\openvp.exe).
rem Needs the .NET 8 SDK. Run it again after pulling changes.
setlocal
cd /d "%~dp0"

echo Building the OpenVP server...
dotnet publish src\OpenVP.Server -c Release -r win-x64 --self-contained false -o dist\OpenVP\server || goto :failed

echo Building the OpenVP desktop app...
dotnet publish src\OpenVP.Desktop -c Release -r win-x64 --self-contained false -o dist\OpenVP || goto :failed

echo.
echo Done: dist\OpenVP\OpenVP.exe
exit /b 0

:failed
echo.
echo Build failed. Is the .NET 8 SDK installed? https://dotnet.microsoft.com/download
pause
exit /b 1
