param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'))
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseExe = Join-Path $repoRoot 'app\release\SpiroLab.exe'
if (-not (Test-Path -LiteralPath $releaseExe)) { throw 'Build the release first.' }
$outRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
$packageId = 'SpiroLab-5.3.1-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture)
$packageRoot = Join-Path $outRoot $packageId
New-Item -ItemType Directory -Path $packageRoot | Out-Null
Copy-Item -LiteralPath $releaseExe -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'app\release\SpiroLab.exe.config') -Destination $packageRoot
New-Item -ItemType Directory -Path (Join-Path $packageRoot 'Docs') | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\standalone') -Destination (Join-Path $packageRoot 'Docs\standalone') -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\parity') -Destination (Join-Path $packageRoot 'Docs\parity') -Recurse
'SpiroLab 5.3.1 - Maneuver selection and Simplified HLine workflow', 'Launch.cmd starts the application.', 'Persian instructions: Docs\standalone\installation-fa.md', 'HLine workflow and limitations: Docs\standalone\hline531-workflow.md', 'HLine authentication is session-only: the operator signs in once in Firefox (including MFA) and SpiroLab reuses that same session. Clinical HTTP writes are disabled pending verified network contract and dedicated authorized test record.', 'National ID lookup is automatic. Send to site has no confirmation dialog. Live writes remain disabled pending the designated test record and verified contract. Right-click for local export.', 'No maneuver-count cap. The visible 40 MiB XML file safety budget still applies.', 'Radio: one site-data maneuver. Checkboxes: up to3 report curves. JSON siteExport and bestReportedValues are separate.', 'Private MIRUSB backup is delivered separately; see Docs\standalone\driver-installation.md.', 'Examples are NON-CLINICAL TEST DATA. Clean-PC and real hardware validation remain pending.' | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $packageRoot 'README.txt')
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
