@echo off
setlocal
if not exist "%~dp0release\MIRParityLab.exe" (
  call "%~dp0build.bat"
  if errorlevel 1 exit /b 1
)
start "" "%~dp0release\MIRParityLab.exe"
