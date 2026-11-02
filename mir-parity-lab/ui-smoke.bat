@echo off
setlocal
set "ROOT=%~dp0"
call "%ROOT%build.bat"
if errorlevel 1 exit /b 1
set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
pushd "%ROOT%"
echo Building UI smoke harness ...
"%CSC%" /nologo /target:exe /platform:x86 /codepage:65001 /optimize+ ^
  /out:release\UiSmoke.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  /r:release\ParityLab.Core.dll ^
  src\App\AssemblyInfo.cs src\App\MainForm.cs src\App\ManualMeasurementForm.cs tools\UiSmoke.cs
if errorlevel 1 goto :fail
echo Running UI smoke test ...
release\UiSmoke.exe
if errorlevel 1 goto :fail
popd
exit /b 0

:fail
echo UI SMOKE FAILED
popd
exit /b 1
