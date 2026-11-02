@echo off
setlocal
set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found: %CSC%
  exit /b 1
)
set "ROOT=%~dp0"
if not exist "%ROOT%release" mkdir "%ROOT%release"
pushd "%ROOT%"

echo [1/3] Building ParityLab.Core.dll ...
"%CSC%" /nologo /target:library /platform:x86 /codepage:65001 /optimize+ ^
  /out:release\ParityLab.Core.dll ^
  /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll ^
  src\Core\Provenance.cs ^
  src\Core\Models.cs ^
  src\Core\HexUtil.cs ^
  src\Core\Privacy.cs ^
  src\Core\ProtocolCatalog.cs ^
  src\Core\ProtocolDecoder.cs ^
  src\Core\CurveCalculator.cs ^
  src\Core\CaptureLoader.cs ^
  src\Core\OfficialLoader.cs ^
  src\Core\UnitConversion.cs ^
  src\Core\ComparisonEngine.cs ^
  src\Core\ReportExporter.cs
if errorlevel 1 goto :fail

echo [2/3] Building MIRParityLab.exe ...
"%CSC%" /nologo /target:winexe /platform:x86 /codepage:65001 /optimize+ ^
  /win32manifest:app.manifest ^
  /out:release\MIRParityLab.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  /r:release\ParityLab.Core.dll ^
  src\App\AssemblyInfo.cs ^
  src\App\Program.cs ^
  src\App\MainForm.cs ^
  src\App\ManualMeasurementForm.cs
if errorlevel 1 goto :fail

copy /Y MIRParityLab.exe.config release\MIRParityLab.exe.config >nul

echo [3/3] Build OK.
echo   %ROOT%release\MIRParityLab.exe
popd
exit /b 0

:fail
echo BUILD FAILED
popd
exit /b 1
