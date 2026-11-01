@echo off
setlocal
REM ---------------------------------------------------------------
REM Build SpiroLab.exe (32-bit, .NET Framework 4.x, no SDK needed)
REM ---------------------------------------------------------------

set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found: %CSC%
  exit /b 1
)

if not exist "%~dp0release" mkdir "%~dp0release"

pushd "%~dp0"

echo Building SpiroLab.exe ...
"%CSC%" /nologo /target:winexe /platform:x86 /codepage:65001 /optimize+ ^
  /win32manifest:app.manifest ^
  /out:release\SpiroLab.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Web.Extensions.dll /r:System.Management.dll ^
  Src\Program.cs ^
  Src\AssemblyInfo.cs ^
  Src\Model.cs ^
  Src\AppSettings.cs ^
  Src\DeploymentSelfTest.cs ^
  Src\ExaminationSnapshot.cs ^
  Src\Expiration.cs ^
  Src\ExamStore.cs ^
  Src\ExamSummary.cs ^
  Src\ResultExport.cs ^
  Src\Ers93.cs ^
  Src\SpiroAnalysis.cs ^
  Src\MirUsb.cs ^
  Src\Device\DeviceSession.cs ^
  Src\Device\DeviceDiagnostics.cs ^
  Src\Device\PacketDecoder.cs ^
  Src\Device\Simulator.cs ^
  Src\Ui\ChartPanel.cs ^
  Src\Ui\MainForm.cs ^
  Src\Ui\ReportForm.cs ^
  Src\Ui\DiagnosticsForm.cs ^
  Src\Ui\SettingsForm.cs ^
  Src\Ui\SpiroReport.cs

if errorlevel 1 goto :fail

REM The application uses MIRUSB directly and does not load mirwsp.dll.

copy /Y SpiroLab.exe.config release\SpiroLab.exe.config >nul

echo.
echo Build OK: %~dp0release\SpiroLab.exe
popd
exit /b 0

:fail
echo BUILD FAILED
popd
exit /b 1
