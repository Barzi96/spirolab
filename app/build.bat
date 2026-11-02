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

set "OUTDIR=release"
set "OUTNAME=SpiroLab.exe"
set "TARGET=winexe"
set "DEFINE="
if /I "%~1"=="test" (
  set "OUTDIR=release-test"
  set "OUTNAME=SpiroLab.Test.dll"
  set "TARGET=library"
  set "DEFINE=/define:SPIROLAB_TEST"
)
if not exist "%~dp0%OUTDIR%" mkdir "%~dp0%OUTDIR%"

pushd "%~dp0"

echo Building %OUTDIR%\%OUTNAME% ...
"%CSC%" /nologo /target:%TARGET% /platform:x86 /codepage:65001 /optimize+ %DEFINE% ^
  /win32manifest:app.manifest ^
  /out:%OUTDIR%\%OUTNAME% ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Web.Extensions.dll /r:System.Management.dll ^
  Src\Program.cs ^
  Src\AssemblyInfo.cs ^
  Src\Model.cs ^
  Src\BestValues.cs ^
  Src\DeviceProvenance.cs ^
  Src\SessionBestValues.cs ^
  Src\AppSettings.cs ^
  Src\DeploymentSelfTest.cs ^
  Src\ExaminationSnapshot.cs ^
  Src\Expiration.cs ^
  Src\ExamStore.cs ^
  Src\ExamSummary.cs ^
  Src\ManeuverAssessment.cs ^
  Src\HLine\HLineClient.cs ^
  Src\HLine\FirefoxSession.cs ^
  Src\HLine\Sqlite.cs ^
  Src\HLine\Submission.cs ^
  Src\HLine\SubmissionTiming.cs ^
  Src\HLine\LiveSubmissionGateway.cs ^
  Src\ResultExport.cs ^
  Src\Ers93.cs ^
  Src\SpiroAnalysis.cs ^
  Src\ClinicVext.cs ^
  Src\MirUsb.cs ^
  Src\Device\DeviceSession.cs ^
  Src\Device\DeviceDiagnostics.cs ^
  Src\Device\PacketDecoder.cs ^
  Src\Device\Simulator.cs ^
  Src\Ui\ChartPanel.cs ^
  Src\Ui\MainForm.cs ^
  Src\Ui\ClinicalStyle.cs ^
  Src\Ui\PatientForm.cs ^
  Src\Ui\HLineForms.cs ^
  Src\Ui\JalaliDateForm.cs ^
  Src\Ui\RadioColumn.cs ^
  Src\Ui\ExaminationDetailsForm.cs ^
  Src\Ui\ReportForm.cs ^
  Src\Ui\DiagnosticsForm.cs ^
  Src\Ui\SettingsForm.cs ^
  Src\Ui\SpiroReport.cs

if errorlevel 1 goto :fail

REM The application uses MIRUSB directly and does not load mirwsp.dll.

copy /Y SpiroLab.exe.config %OUTDIR%\%OUTNAME%.config >nul

echo.
echo Build OK: %~dp0%OUTDIR%\%OUTNAME%
popd
exit /b 0

:fail
echo BUILD FAILED
popd
exit /b 1
