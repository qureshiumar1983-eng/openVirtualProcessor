@echo off
rem Starts OpenVP, building desktop\dist\OpenVP.exe first if it isn't there.
rem You can also just run desktop\dist\OpenVP.exe (or copy it anywhere and pin it to the taskbar).
cd /d "%~dp0"
if not exist "desktop\dist\OpenVP.exe" call publish-desktop.bat || exit /b 1
start "" "desktop\dist\OpenVP.exe"
