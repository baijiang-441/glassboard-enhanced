@echo off
setlocal
cd /d "%~dp0"
if not exist "GlassBoard.exe" (
  echo GlassBoard.exe is missing. Extract the entire ZIP first.
  pause
  exit /b 1
)
start "" "%~dp0GlassBoard.exe"
