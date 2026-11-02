@echo off
setlocal
set "ROOT=%~dp0"
call "%ROOT%build.bat"
if errorlevel 1 exit /b 1
set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
pushd "%ROOT%"
echo Building test harness ...
"%CSC%" /nologo /target:exe /platform:x86 /codepage:65001 /optimize+ ^
  /out:release\ParityLab.Tests.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll ^
  /r:release\ParityLab.Core.dll ^
  tests\ParityLabTests.cs
if errorlevel 1 goto :fail
echo Running tests ...
release\ParityLab.Tests.exe
if errorlevel 1 goto :fail
popd
exit /b 0

:fail
echo TEST FAILED
popd
exit /b 1
