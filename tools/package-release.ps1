param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'))
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseExe = Join-Path $repoRoot 'app\release\SpiroLab.exe'
$testAssembly = Join-Path $repoRoot 'app\release-test\SpiroLab.Test.dll'
if (-not (Test-Path -LiteralPath $releaseExe)) { throw 'Build the production release first (app\build.bat).' }
if (-not (Test-Path -LiteralPath $testAssembly)) { throw 'Build the isolated test assembly first (app\build.bat test).' }
$outRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
$packageId = 'SpiroLab-5.4.5-MIR-ManeuverEnd-FieldTest-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture)
$packageRoot = Join-Path $outRoot $packageId
New-Item -ItemType Directory -Path $packageRoot | Out-Null
Copy-Item -LiteralPath $releaseExe -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'app\release\SpiroLab.exe.config') -Destination $packageRoot
New-Item -ItemType Directory -Path (Join-Path $packageRoot 'Docs') | Out-Null
Copy-Item -Path (Join-Path $repoRoot 'docs\operator\*') -Destination (Join-Path $packageRoot 'Docs') -Recurse
$versionText = @(
  'SpiroLab 5.4.5',
  'آماده آزمون میدانی پایان مانور',
  ('Source commit: {0}' -f (& git -C $repoRoot rev-parse HEAD)),
  ('Build UTC: {0}' -f [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)),
  'Executable: SpiroLab.exe (x86, .NET Framework 4.x)',
  'No MIR driver, proprietary software, test data, test executable, source or logs are bundled.',
  'MIRUSB.SYS must be installed from the official MIR distribution.',
  'Hardware, clean-PC, BTPS and production HLine validation: PENDING.'
) -join "`r`n"
$versionText | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $packageRoot 'Version.txt')
$readme = @(
  'SpiroLab 5.4.5 - MIR maneuver-end field test',
  'آماده آزمون میدانی پایان مانور',
  '',
  'Run Launch.cmd. The app starts without an installer.',
  'Read Docs\README-fa.md first, then Docs\daily-workflow-fa.md.',
  'MIRUSB driver is NOT included; install the compatible driver from the official MIR distribution.',
  'Portable data is stored under %APPDATA%\SpiroLab.',
  '',
  'Docs: README-fa.md, daily-workflow-fa.md, troubleshooting-fa.md, release-notes-mir-maneuver-end.md.',
  'No simulated patient data, test executable, benchmark, log or source file is part of this package.',
  'Hardware, clean-PC, BTPS and production HLine validation remain PENDING.'
) -join "`r`n"
$readme | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $packageRoot 'README.txt')
@('@echo off', 'start "" "%~dp0SpiroLab.exe"') | Set-Content -Encoding ASCII -LiteralPath (Join-Path $packageRoot 'Launch.cmd')

# Validation-only test host. It is compiled outside the package and never copied in.
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw 'In-box csc.exe not found.' }
$selfTestHost = Join-Path $repoRoot 'app\release-test\SpiroLab.SelfTest.exe'
& $csc /nologo /target:exe /platform:x86 /codepage:65001 /out:$selfTestHost /r:$testAssembly /r:System.dll /r:System.Core.dll (Join-Path $repoRoot 'app\Tests\SelfTestHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Validation self-test host compilation failed.' }
$validationRoot = Join-Path $outRoot ($packageId + '-validation')
$p = Start-Process -FilePath $selfTestHost -ArgumentList @(('"' + $validationRoot + '"')) -WorkingDirectory $repoRoot -WindowStyle Hidden -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'Validation self-test failed; inspect the validation folder.' }
$sample = Get-ChildItem -LiteralPath $validationRoot -Directory -Filter 'Examination-*' | Select-Object -First 1
if (-not $sample) { throw 'Validation self-test produced no report.' }
$jpeg = Get-Item -LiteralPath (Join-Path $sample.FullName 'Spirometry-Report.jpg')
if ($jpeg.Length -gt 5MB) { throw 'Validation JPEG exceeds the HLine safety limit.' }

# Distribution whitelist: no vendor files, compiler, sources, test executable, fixtures or logs.
$forbidden = Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object {
  $_.Extension -in '.sys','.inf','.cat','.dll','.cs','.log','.jpg','.json','.zip','.py','.ps1','.bat' -or
  ($_.Extension -eq '.exe' -and $_.Name -ne 'SpiroLab.exe')
}
if ($forbidden) { throw ('Non-allowlisted file found in distribution: ' + ($forbidden.Name -join ', ')) }
$manifest = Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash + '  ' + $_.Path.Substring($packageRoot.Length + 1) }
$manifest | Set-Content -Encoding ASCII -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt')
$zipPath = $packageRoot + '-Portable.zip'
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath
[pscustomobject]@{
  PackageDirectory = $packageRoot
  PortableZip = $zipPath
  ExecutableSha256 = (Get-FileHash -LiteralPath $releaseExe -Algorithm SHA256).Hash
  ZipSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
  ValidationDirectory = $validationRoot
  ValidationJpegBytes = $jpeg.Length
  CleanWindowsAcceptance = 'PENDING'
  Hardware = 'HARDWARE VALIDATION PENDING'
  ProductionHLine = 'PENDING'
} | ConvertTo-Json
