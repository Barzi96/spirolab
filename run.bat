@echo off
if not exist "%~dp0app\release\SpiroLab.exe" (
  echo Release executable missing. Build on the development PC or extract the complete portable package.
  exit /b 1
)
start "" "%~dp0app\release\SpiroLab.exe"
