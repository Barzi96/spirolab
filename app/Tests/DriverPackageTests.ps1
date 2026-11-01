param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$installer=Join-Path $repo 'tools\Install-MirDriver.ps1'
$output=Join-Path $repo ('app\driver-test-output\'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
& $installer -PackageDirectory $PackageDirectory
Write-Output 'PASS private signed package inspection (no install)'
foreach($case in 'tampered_file','escaping_path','wrong_architecture','missing_component','escaping_inf') {
  $copy=Join-Path $output $case
  Copy-Item -LiteralPath $PackageDirectory -Destination $copy -Recurse
  $manifestPath=Join-Path $copy 'driver-manifest.json'
  $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
  $expected=''
  switch($case) {
    'tampered_file' { $sys=$manifest.Files | Where-Object { $_.Path -like '*.sys' } | Select-Object -First 1; $path=Join-Path $copy $sys.Path; $bytes=[IO.File]::ReadAllBytes($path); $bytes[0]=$bytes[0] -bxor 1; [IO.File]::WriteAllBytes($path,$bytes); $expected='Hash mismatch' }
    'escaping_path' { $manifest.Files[0].Path='..\outside.cat'; $expected='Manifest path escapes' }
    'wrong_architecture' { $manifest.Architecture='unsupported'; $expected='architecture does not match' }
    'missing_component' { $manifest.Files=@($manifest.Files | Where-Object { $_.Path -notlike '*.sys' }); $expected='Manifest must cover' }
    'escaping_inf' { $manifest.OriginalInf='..\outside.inf'; $expected='Installation INF must be included' }
  }
  $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
  $rejected=$false
  try { & $installer -PackageDirectory $copy } catch { if($_.Exception.Message -like ('*'+$expected+'*')) { $rejected=$true } else { throw } }
  if(!$rejected) { throw ('Expected rejection: '+$case) }
  Write-Output ('PASS '+$case+' rejected before any driver operation')
}
Write-Output 'ALL 6 DRIVER PACKAGE INSPECTION CHECKS PASSED; DESTINATION INSTALLATION PENDING'
