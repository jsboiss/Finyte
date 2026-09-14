@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\dev.ps1" start
if errorlevel 1 (
  pause
  exit /b 1
)
start "" "http://127.0.0.1:5186/"
