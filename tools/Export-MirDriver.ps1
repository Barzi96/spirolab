param([Parameter(Mandatory=$true)][string]$Destination,[string]$HardwareId='USB\VID_152B&PID_0002')
$ErrorActionPreference='Stop'
$driverRecords=Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{36fc9e60-c465-11cf-8056-444553540000}' -ErrorAction SilentlyContinue | ForEach-Object { Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue } | Where-Object { $_.MatchingDeviceId -eq $HardwareId -and $_.InfPath -match '^oem\d+\.inf$' }
$published=@($driverRecords.InfPath | Sort-Object -Unique)
if($published.Count -ne 1) { throw 'Exactly one matching installed package is required; inspect Device Manager before export.' }
$root=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $root) { if(Get-ChildItem -LiteralPath $root -Force) { throw 'Destination must be empty; existing backups are preserved.' } }
New-Item -ItemType Directory -Force -Path $root | Out-Null
& pnputil.exe /export-driver $published[0] $root
if($LASTEXITCODE -ne 0) { throw 'Driver-store export failed.' }
$inf=@(Get-ChildItem -LiteralPath $root -Recurse -Filter '*.inf')
if($inf.Count -ne 1) { throw 'Expected one exported INF.' }
$text=Get-Content -LiteralPath $inf[0].FullName -Raw
if($text.IndexOf($HardwareId,[StringComparison]::OrdinalIgnoreCase) -lt 0) { throw 'Exported INF does not match hardware ID.' }
$catalogs=@(Get-ChildItem -LiteralPath $root -Recurse -Filter '*.cat')
$sys=@(Get-ChildItem -LiteralPath $root -Recurse -Filter '*.sys')
if(!$catalogs -or !$sys) { throw 'Incomplete INF package: SYS and CAT are required.' }
$signed=@($catalogs+$sys | ForEach-Object { $sig=Get-AuthenticodeSignature -LiteralPath $_.FullName; if($sig.Status -ne 'Valid') { throw ('Signature not valid: '+$_.Name) }; [pscustomobject]@{File=$_.FullName.Substring($root.Length+1);Status=[string]$sig.Status;Signer=$sig.SignerCertificate.Subject} })
$files=@(Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object { [pscustomobject]@{Path=$_.FullName.Substring($root.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
[pscustomobject]@{PublishedInf=$published[0];OriginalInf=$inf[0].Name;HardwareId=$HardwareId;Version=$driverRecords[0].DriverVersion;Provider=$driverRecords[0].ProviderName;Architecture=$(if([Environment]::Is64BitOperatingSystem){'x64'}else{'x86'});Signatures=$signed;Files=$files;DestinationInstallation='PENDING';LiveCommunication='PENDING'} | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'driver-manifest.json')
'Private backup for the device owner. Run Install-MirDriver.ps1 -PackageDirectory <this folder> first without -Install to inspect. Install separately as administrator on the destination PC. No MIR desktop application is included. Export success is not destination/hardware acceptance.' | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'README.txt')
Get-Content -LiteralPath (Join-Path $root 'driver-manifest.json')
