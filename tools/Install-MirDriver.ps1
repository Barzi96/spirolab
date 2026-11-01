param([Parameter(Mandatory=$true)][string]$PackageDirectory,[switch]$Install)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\')
$manifest=Get-Content -Raw -LiteralPath (Join-Path $root 'driver-manifest.json') | ConvertFrom-Json
if($manifest.HardwareId -ne 'USB\VID_152B&PID_0002') { throw 'Unexpected hardware package.' }
$arch=if([Environment]::Is64BitOperatingSystem){'x64'}else{'x86'}
if($manifest.Architecture -ne $arch) { throw 'Driver architecture does not match this Windows installation.' }
if(@($manifest.Files | Where-Object { [IO.Path]::GetExtension($_.Path) -eq '.inf' }).Count -ne 1 -or @($manifest.Files | Where-Object { [IO.Path]::GetExtension($_.Path) -eq '.sys' }).Count -lt 1 -or @($manifest.Files | Where-Object { [IO.Path]::GetExtension($_.Path) -eq '.cat' }).Count -lt 1) { throw 'Manifest must cover INF, SYS and CAT components.' }
foreach($file in $manifest.Files) {
  $path=[IO.Path]::GetFullPath((Join-Path $root $file.Path))
  if(!$path.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escapes package.' }
  if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.SHA256) { throw ('Hash mismatch: '+$file.Path) }
  if([IO.Path]::GetExtension($path) -in '.cat','.sys') { if((Get-AuthenticodeSignature -LiteralPath $path).Status -ne 'Valid') { throw ('Signature validation failed: '+$file.Path) } }
}
$inf=[IO.Path]::GetFullPath((Join-Path $root $manifest.OriginalInf))
if(!$inf.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or @($manifest.Files | Where-Object { $_.Path -eq $manifest.OriginalInf }).Count -ne 1) { throw 'Installation INF must be included in the verified package manifest.' }
if((Get-Content -Raw -LiteralPath $inf).IndexOf($manifest.HardwareId,[StringComparison]::OrdinalIgnoreCase) -lt 0) { throw 'INF does not match expected hardware.' }
$devices=@(Get-CimInstance Win32_PnPEntity | Where-Object { $_.PNPDeviceID -like '*VID_152B&PID_0002*' -and $_.ConfigManagerErrorCode -ne 45 })
foreach($device in $devices) {
  if($device.Service -and $device.Service -ne 'MIRUSB') { throw 'An existing different driver is bound. No replacement performed; obtain approval and recovery instructions first.' }
  if($device.Service -eq 'MIRUSB' -and $device.ConfigManagerErrorCode -eq 0) { Write-Output 'MIRUSB already bound and ready. No unnecessary installation performed.'; return }
}
Write-Output ('Verified private package: '+$manifest.Version+'; '+$arch+'; INF '+$inf)
if(!$Install) { Write-Output 'Inspection only. To install on the intended PC, run elevated with -Install. No drivers changed.'; return }
$admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(!$admin) { throw 'Administrator PowerShell is required for installation.' }
& pnputil.exe /add-driver $inf /install
if($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 3010) { throw ('Installation failed: '+$LASTEXITCODE) }
Write-Output 'Driver installation/staging finished. Verify Device Manager binding, then application diagnostics and live packets. This is NOT proof of working spirometry.'
