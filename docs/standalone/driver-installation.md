# Private MIRUSB backup and installation — 5.1

This workflow is for the owner's existing device and private PCs. The portable application and driver backup are separate packages. Neither requires the MIR desktop application. This phase exported and inspected the package; it did not install, replace or rebind a driver.

## Export verified on the development PC

Exact matching installed package: `oem68.inf`, original `mirusb.inf`, M.I.R. srl, version `2.0.0.9`, DriverVer `12/07/2009`. Hardware: `USB\VID_152B&PID_0002`, observed revision `1021`. Source: `C:\Windows\System32\DriverStore\FileRepository\mirusb.inf_amd64_7c89a49ba4c82625`. Historical device instances and interface GUID registration are recorded in the private package's `private-device-evidence.json`; they are excluded from Git. No present device was reported by Windows inventory during this phase.

Reproduce in Administrator PowerShell from the project root, using a NEW or empty destination:

```powershell
.\tools\Export-MirDriver.ps1 -Destination C:\SpiroLab-Driver-Backup
```

The helper identifies one exact registry binding, runs `pnputil /export-driver`, verifies matching INF, SYS and CAT, checks signatures and writes SHA256 manifest plus README. It refuses to overwrite a nonempty backup. Export contains `mirusb.inf`, `miramd64.cat`, `amd64\mirusb.sys`; SYS and CAT Authenticode returned `Valid`, Microsoft Windows Hardware Compatibility Publisher. INF is covered by its catalog and separately hashed, rather than falsely described as an individually signed executable. The exported package is x64 only; x86 INF sections do not supply a 32-bit kernel binary.

## On the second Windows 10 x64 PC

1. Extract the separate private-driver ZIP to `C:\SpiroLab-MIRUSB-5.1`. Keep `Install-MirDriver.ps1` beside `DriverPackage`.
2. Inspect without changing Windows:

```powershell
C:\SpiroLab-MIRUSB-5.1\Install-MirDriver.ps1 -PackageDirectory C:\SpiroLab-MIRUSB-5.1\DriverPackage
```

3. Open PowerShell as Administrator. Install the verified private package explicitly:

```powershell
C:\SpiroLab-MIRUSB-5.1\Install-MirDriver.ps1 -PackageDirectory C:\SpiroLab-MIRUSB-5.1\DriverPackage -Install
```

This runs `pnputil /add-driver "C:\SpiroLab-MIRUSB-5.1\DriverPackage\mirusb.inf" /install`. It validates architecture, contained paths, component hashes and signatures first. An already ready MIRUSB binding is left alone. A different bound service stops installation for review. No forced replacement, removal of other packages, firmware operation or signature bypass is included. If PowerShell policy blocks an unsigned helper script, follow the administrator's existing policy; the exact PnPUtil command above is the Windows installation operation.

4. Connect the device and verify Device Manager: matching hardware ID, MIRUSB service, provider/version and no problem icon. `pnputil /enum-devices /connected` can corroborate the present instance. A staged INF alone is not proof of binding.
5. Run the portable EXE as an ordinary user. Open «وضعیت دستگاه». Verify PnP, driver, opened interface, command response, live packets and decoded samples separately. Perform a live maneuver and compare its independent report with the device result. Record interruption/reconnect behavior.

Successful export, signature checks and inspection on this host are established. Installation/binding, Windows 10 clean-PC acceptance, actual live results and native cancellation behavior on the second PC remain PENDING.
