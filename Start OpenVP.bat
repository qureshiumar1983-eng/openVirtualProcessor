@echo off
rem Runs OpenVP in dev mode: the Electron app on your source code. Changes to the web pages show up in the
rem window right away; C# changes rebuild and restart the server automatically (about 10-15 seconds).
rem Needs the .NET 8 SDK and Node.js. Close the OpenVP window to stop. To build a standalone OpenVP.exe,
rem run publish-desktop.bat instead.
cd /d "%~dp0desktop"
if not exist node_modules (
  call npm install --no-audit --no-fund || (pause & exit /b 1)
)
call npm start
