@echo off
rem Builds dist\OpenVP.exe: the whole app in one file (runs without installing .NET).
rem Building needs the .NET 8 SDK; running the exe doesn't.
setlocal
cd /d "%~dp0"
dotnet publish src\OpenVP.Desktop -c Release -r win-x64 -o dist || goto :failed
echo.
echo Done: dist\OpenVP.exe
exit /b 0

:failed
echo.
echo Build failed. Is the .NET 8 SDK installed? https://dotnet.microsoft.com/download
pause
exit /b 1
