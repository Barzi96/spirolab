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
'SpiroLab 5.3.1 - MIR measurement provenance and session BEST VALUES', 'Launch.cmd starts the application.', 'Persian instructions: Docs\standalone\installation-fa.md', 'BEST VALUES rules and evidence: Docs\standalone\best-values-531.md', 'Selected-three/delete/HLine timing contract: Docs\standalone\mir-selected-three-531.md', 'Refinement regression report: Docs\standalone\mir-refinement-regression-531.md', 'HLine workflow and limitations: Docs\standalone\hline531-workflow.md', 'HLine write implementation and risks: Docs\standalone\hline-write-implementation.md', 'HLine live test evidence: Docs\standalone\live-test-results.md', 'GUI mock end-to-end evidence: Docs\standalone\gui-e2e-results.md', 'Field-testing checklist: Docs\standalone\field-testing-checklist.md', 'HLine authentication is session-only: the operator signs in once in Firefox (including MFA) and SpiroLab reuses that same session. The normal Send button uses the displayed patient''s re-verified latest medical record; no test-mode switch, test record, or extra production activation is required. Automated test builds may use a designated record through internal APIs, but the released app has no such dependency.', 'National ID lookup is automatic. Send to site has no confirmation dialog. Post-save spirometry module status NoData is a visible failure. One owner-authorized restore to a designated historical test record passed; production/general clinical validation remains PENDING.', 'No maneuver-count cap. The visible 40 MiB XML file safety budget still applies.', 'There is no primary/site radio. BEST FVC, FEV1, PEF and FET are independent maxima; FEV1/FVC = best FEV1 / best FVC; FEF25-75 comes from the eligible maneuver with the largest FVC+FEV1. Workflow-4 examinations select at most three final maneuvers (quality then FVC+FEV1, or operator override); the selected set is the authoritative source for BEST VALUES, JPG, JSON, HLine and the displayed curves. A manual override is not silently replaced; a compact Delete/Undo action is provided.', 'HLine: fvc_amount/fev1_amount/pef_amount/fef2575_amount and each percent are the selected-three aggregate. vext is unchanged ClinicVext (largest real EVol strictly between 0 and 200 mL) over all active real maneuvers.', 'Device-reported result blocks are used only when bound to the exact completed capture; reconstructed software values remain separately labelled in local JSON diagnostics. Exact MIR firmware parity, BTPS and paired hardware validation are PENDING.', 'Private MIRUSB backup is delivered separately; see Docs\standalone\driver-installation.md.', 'Examples are NON-CLINICAL TEST DATA. Clean-PC and real hardware validation remain pending.' | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $packageRoot 'README.txt')
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
