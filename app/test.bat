@echo off
REM ---------------------------------------------------------------
REM SpiroLab test runner.
REM Usage: test.bat [path\to\mir-capture.csv]
REM
REM The real MIR capture file is OPTIONAL and never part of the repo.
REM Without it the real-capture checks are reported SKIPPED, while every
REM independent suite still runs and must pass. With it, the real replay
REM facts are checked as well.
REM ---------------------------------------------------------------
setlocal EnableExtensions EnableDelayedExpansion
pushd "%~dp0"

set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "REFS=/r:release-test\SpiroLab.Test.dll /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll"
set "LOGDIR=%~dp0test-logs"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"
set "CAPTURE=%~1"
if defined CAPTURE if not exist "%CAPTURE%" echo WARNING: explicit capture file not found: %CAPTURE%

call build.bat test
if errorlevel 1 goto :buildfail
REM build.bat creates release\ and its config; copy the test assembly after that
REM so a fresh clone with no pre-existing release directory also works.
call build.bat
if errorlevel 1 goto :buildfail
copy /Y release-test\SpiroLab.Test.dll release\SpiroLab.Test.dll >nul

set /a TOTALSUITES=0
set /a TOTALPASS=0
set /a TOTALFAIL=0
set /a TOTALSKIP=0
set "FAILEDSUITES="

echo.
echo === SpiroLab test suites ===
for %%S in (RegressionTests StandaloneTests PrecisionTests ManeuverSelectionTests ClinicalUiTests MirBestValuesTests RefinementTests HLinePhaseTests HLineIntegrationTests HLineWriteTests DeviceProvenanceTests) do call :runsuite %%S
goto :summary

:runsuite
set "SUITE=%~1"
set /a TOTALSUITES+=1
set "OUT=%~dp0test-output"
if /I "%SUITE%"=="StandaloneTests" set "OUT=%~dp0standalone-test-output"
if /I "%SUITE%"=="PrecisionTests" set "OUT=%~dp0precision-test-output"
if /I "%SUITE%"=="ManeuverSelectionTests" set "OUT=%~dp0maneuver-selection-test-output"
if /I "%SUITE%"=="ClinicalUiTests" set "OUT=%~dp0clinical-ui-test-output"
if /I "%SUITE%"=="MirBestValuesTests" set "OUT=%~dp0mir-best-values-test-output"
if /I "%SUITE%"=="RefinementTests" set "OUT=%~dp0refinement-test-output"
if /I "%SUITE%"=="HLinePhaseTests" set "OUT=%~dp0hline-test-output\HLinePhaseTests"
if /I "%SUITE%"=="HLineIntegrationTests" set "OUT=%~dp0hline-test-output\HLineIntegrationTests"
if /I "%SUITE%"=="HLineWriteTests" set "OUT=%~dp0hline-test-output\HLineWriteTests"
if /I "%SUITE%"=="DeviceProvenanceTests" set "OUT=%~dp0device-provenance-test-output"

set "LOG=%LOGDIR%\%SUITE%.log"
"%CSC%" /nologo /target:exe /platform:x86 /codepage:65001 /out:release\%SUITE%.exe %REFS% Tests\%SUITE%.cs >"%LOGDIR%\%SUITE%.build.log" 2>&1
if errorlevel 1 (
  echo   %SUITE%  BUILD-FAILED  see test-logs\%SUITE%.build.log
  set "FAILEDSUITES=!FAILEDSUITES! %SUITE%"
  set /a TOTALFAIL+=1
  goto :eof
)

if /I "%SUITE%"=="RegressionTests" (
  if defined CAPTURE (
    release\%SUITE%.exe "%OUT%" "%CAPTURE%" >"%LOG%" 2>&1
  ) else (
    release\%SUITE%.exe "%OUT%" >"%LOG%" 2>&1
  )
) else (
  release\%SUITE%.exe "%OUT%" >"%LOG%" 2>&1
)
set "CODE=!errorlevel!"

call :count "%LOG%" "PASS " PASSCOUNT
call :count "%LOG%" "SKIPPED " SKIPCOUNT
set "FAILCOUNT=0"
if not "%CODE%"=="0" set "FAILCOUNT=1"
if not "%CODE%"=="0" set "FAILEDSUITES=!FAILEDSUITES! %SUITE%"
set /a TOTALPASS+=PASSCOUNT
set /a TOTALFAIL+=FAILCOUNT
set /a TOTALSKIP+=SKIPCOUNT
echo   %SUITE%  PASS=%PASSCOUNT%  FAIL=%FAILCOUNT%  SKIPPED=%SKIPCOUNT%  exit=%CODE%
goto :eof

:count
REM %1 file, %2 line prefix, %3 result variable name
set "COUNT=0"
for /f %%C in ('type "%~1" ^| findstr /b /c:"%~2" ^| find /c /v ""') do set "COUNT=%%C"
set "%~3=%COUNT%"
goto :eof

:summary
echo.
echo ===============================================================
echo TEST SUMMARY
echo   suites run : %TOTALSUITES%
echo   PASSED     : %TOTALPASS%
echo   FAILED     : %TOTALFAIL%
echo   SKIPPED    : %TOTALSKIP%
if defined CAPTURE (
  echo   capture    : %CAPTURE%
) else (
  echo   capture    : not supplied - real-capture checks SKIPPED
)
if defined FAILEDSUITES echo   failed     :%FAILEDSUITES%
if not "%TOTALFAIL%"=="0" goto :fail
if not "%TOTALSKIP%"=="0" echo   NOTE: SKIPPED checks are not PASSED.
echo Logs: %LOGDIR%
popd
exit /b 0

:fail
echo Logs: %LOGDIR%
popd
exit /b 1

:buildfail
echo BUILD FAILED - no suite result is valid.
popd
exit /b 1
