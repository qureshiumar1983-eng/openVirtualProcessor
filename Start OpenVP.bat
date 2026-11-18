@echo off
rem Starts the OpenVP desktop app, building it first if needed.
rem After the first build you can also just double-click dist\OpenVP\OpenVP.exe (or pin it to the taskbar).
cd /d "%~dp0"
if not exist "dist\OpenVP\OpenVP.exe" call publish-desktop.bat || exit /b 1
start "" "dist\OpenVP\OpenVP.exe"
