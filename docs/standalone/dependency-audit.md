# Dependency evidence — 5.1 correction to the 5.0 record

Baseline 5.0 source and portable release were inspected, backed up and verified with all 138 original software checks before changes. This development host is Windows 10 x64 build19045 and has had MIR software installed; it is not a clean acceptance PC.

Runtime architecture remains Windows Forms / .NET Framework4.x / x86 with system managed references, kernel32.dll and setupapi.dll. No MIR desktop EXE, DLL, database, activation or application registry dependency was introduced. Windows driver registration is still used. USB IOCTL 0x222450 requires MIRUSB; WinUSB/libusb was not substituted.

The practical driver-export task is completed for the owner's private deployment. Exact oem68.inf / mirusb.inf package, M.I.R. srl 2.0.0.9, was exported from DriverStore with PnPUtil. Complete x64 INF/SYS/CAT components, SHA256 and valid SYS/CAT signatures were verified. The private package is separate from the portable EXE and excluded from Git. See [driver-installation.md](driver-installation.md). No driver installation, rebinding or security changes were performed.

The second PC's original failure cannot be assigned a proven root cause without diagnostic evidence. Missing compatible MIRUSB binding is plausible; current inventory on this host reports no present matching device. Historical instances and interface configuration are kept privately. Clean Windows installation/binding and real live tests remain pending.

Required materials found and inspected: current source and v5 portable distribution, both Downloads/spiro.jpg and spiro2.jpg, local spiro.htm, v5 JPG/schema2 JSON, docs/standalone, tests and USB diagnostics. No required reference in the 5.1 prompt was missing. Printed identity/stamps were not copied into fixtures or Git.

Git now exists locally; baseline commit cc7ed58 and pre-edit phase-backups/before-5.1.zip preserve previous work. Private data, captures, build products and driver binaries are ignored. No push.
