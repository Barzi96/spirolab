@echo off
setlocal
pushd "%~dp0"
call build.bat
if errorlevel 1 goto :fail
"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:exe /platform:x86 /codepage:65001 /out:release\RegressionTests.exe /r:release\SpiroLab.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll Tests\RegressionTests.cs
if errorlevel 1 goto :fail
release\RegressionTests.exe test-output ..\probe\mirprobe\capture-14050809-133557.csv
if errorlevel 1 goto :fail
"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:exe /platform:x86 /codepage:65001 /out:release\StandaloneTests.exe /r:release\SpiroLab.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll Tests\StandaloneTests.cs
if errorlevel 1 goto :fail
release\StandaloneTests.exe standalone-test-output
if errorlevel 1 goto :fail
"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:exe /platform:x86 /codepage:65001 /out:release\PrecisionTests.exe /r:release\SpiroLab.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll Tests\PrecisionTests.cs
if errorlevel 1 goto :fail
release\PrecisionTests.exe precision-test-output
if errorlevel 1 goto :fail
"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:exe /platform:x86 /codepage:65001 /out:release\ManeuverSelectionTests.exe /r:release\SpiroLab.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll Tests\ManeuverSelectionTests.cs
if errorlevel 1 goto :fail
release\ManeuverSelectionTests.exe maneuver-selection-test-output
if errorlevel 1 goto :fail
"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:exe /platform:x86 /codepage:65001 /out:release\ClinicalUiTests.exe /r:release\SpiroLab.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll Tests\ClinicalUiTests.cs
if errorlevel 1 goto :fail
release\ClinicalUiTests.exe clinical-ui-test-output
if errorlevel 1 goto :fail
"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:exe /platform:x86 /codepage:65001 /out:release\MirBestValuesTests.exe /r:release\SpiroLab.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll Tests\MirBestValuesTests.cs
if errorlevel 1 goto :fail
release\MirBestValuesTests.exe mir-best-values-test-output
if errorlevel 1 goto :fail
for %%T in (HLinePhaseTests HLineIntegrationTests HLineWriteTests) do (
  "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:exe /platform:x86 /codepage:65001 /out:release\%%T.exe /r:release\SpiroLab.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll Tests\%%T.cs
  if errorlevel 1 goto :fail
  release\%%T.exe hline-test-output\%%T
  if errorlevel 1 goto :fail
)
popd
exit /b 0
:fail
popd
exit /b 1
