param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'))
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseExe = Join-Path $repoRoot 'app\release\SpiroLab.exe'
if (-not (Test-Path -LiteralPath $releaseExe)) { throw 'Build the release first.' }
$outRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
$packageId = 'SpiroLab-5.0-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture)
$packageRoot = Join-Path $outRoot $packageId
New-Item -ItemType Directory -Path $packageRoot | Out-Null
Copy-Item -LiteralPath $releaseExe -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'app\release\SpiroLab.exe.config') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\standalone') -Destination (Join-Path $packageRoot 'Docs') -Recurse
'@echo off', 'start "" "%~dp0SpiroLab.exe"' | Set-Content -Encoding ASCII -LiteralPath (Join-Path $packageRoot 'Launch.cmd')
$sampleRoot = Join-Path $outRoot ($packageId + '-selftest')
$p = Start-Process -FilePath (Join-Path $packageRoot 'SpiroLab.exe') -ArgumentList @('--self-test', ('"' + $sampleRoot + '"')) -WorkingDirectory $packageRoot -WindowStyle Hidden -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'Packaged self-test failed; inspect the self-test folder.' }
$sample = Get-ChildItem -LiteralPath $sampleRoot -Directory -Filter 'Examination-*' | Select-Object -First 1
New-Item -ItemType Directory -Path (Join-Path $packageRoot 'Examples') | Out-Null
Copy-Item -LiteralPath (Join-Path $sample.FullName 'Spirometry-Report.jpg') -Destination (Join-Path $packageRoot 'Examples')
Copy-Item -LiteralPath (Join-Path $sample.FullName 'Spirometry-Data.json') -Destination (Join-Path $packageRoot 'Examples')
Copy-Item -LiteralPath (Join-Path $sampleRoot 'selftest-result.txt') -Destination (Join-Path $packageRoot 'Docs\packaged-selftest.txt')
# Whitelist the distribution: no vendor files, compiler, sources or test executables.
$forbidden = Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object { $_.Extension -in '.sys','.inf','.cat','.dll','.cs' -or ($_.Extension -eq '.exe' -and $_.Name -ne 'SpiroLab.exe') }
if ($forbidden) { throw 'Non-allowlisted file found in distribution.' }
Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash + '  ' + $_.Path.Substring($packageRoot.Length + 1) } | Set-Content -Encoding ASCII -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt')
$zipPath = $packageRoot + '-Portable.zip'
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath
[pscustomobject]@{ PackageDirectory=$packageRoot; PortableZip=$zipPath; SelfTest=$sampleRoot; CleanWindowsAcceptance='PENDING'; Hardware='HARDWARE VALIDATION PENDING' } | ConvertTo-Json
