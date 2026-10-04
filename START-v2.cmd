@echo off
setlocal
cd /d "%~dp0"
if not exist "artifacts\windows-v2-desktop-047\PropTest.Desktop.exe" (
  echo Build first: powershell -File scripts\build.ps1 -Publish
  pause
  exit /b 1
)
start "" "artifacts\windows-v2-desktop-047\PropTest.Desktop.exe"
