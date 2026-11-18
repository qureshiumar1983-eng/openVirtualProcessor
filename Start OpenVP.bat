@echo off
rem Starts OpenVP, building dist\OpenVP.exe first if it isn't there.
rem You can also just run dist\OpenVP.exe (or copy it anywhere and pin it to the taskbar).
cd /d "%~dp0"
if not exist "dist\OpenVP.exe" call publish-desktop.bat || exit /b 1
start "" "dist\OpenVP.exe"
