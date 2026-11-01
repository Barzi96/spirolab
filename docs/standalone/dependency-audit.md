# Stage A — dependency audit (2026-11-01)

Host: Windows 10 Pro for Workstations 10.0.19045 x64. This is the development PC and has previously had MIR software installed; it is NOT a clean acceptance environment.

The active x86 .NET Framework Windows Forms EXE imports kernel32.dll and setupapi.dll. Managed assemblies are Windows .NET Framework assemblies. The active USB code does not load mirwsp.dll, any MIR EXE, installation database, activation state, background application or MIR application registry settings. USB enumeration reads Windows device/driver registration only.

**Proven prerequisite:** the current vendor IOCTL 0x222450 requires the MIRUSB driver interface. Merely removing mirwsp.dll does not remove this requirement. WinUSB is a different API/driver contract and is not a drop-in replacement. Hardware compatible IDs recorded on this host are vendor class 00; no MS_COMP_WINUSB binding was found in those historical device records.

Installed host driver: MIRUSB.sys; version 2.0.0.9; vendor M.I.R. srl; INF oem68.inf; DriverVer 2009-12-07; kernel image PE machine 0x8664 (x64). Authenticode status on this PC: Valid; signer Microsoft Windows Hardware Compatibility Publisher. INF declares separate NTx86 and NTamd64 catalogs, mirx86.cat and miramd64.cat. Historical binding: USB VID 152B / PID 0002, spirolab III. Service observed stopped. No current signed PnP device entry was returned during this audit. These findings do not establish working live spirometry or availability of an x86 driver on a second PC.

**Likely reason for the second-PC problem:** installing the vendor desktop application installed the required MIRUSB driver. **Not proven:** the second PC's exact A/B/C/D/E failure, its runtime, driver version/signature, binding and transport trace were not supplied. Do not assert a definitive diagnosis for that PC.

**Distribution blocker:** an authorized independently downloadable signed driver package and redistribution permission have NOT been established. Official MIR manuals describe vendor driver installation, but this is not a grant to redistribute installed binaries. No proprietary SYS, INF, CAT, DLL or vendor EXE is bundled. Request an authorized standalone Windows 10 driver for the exact model/firmware from MIR support (https://spirometry.com/). Confirm license, architecture and signed catalog before deployment. No driver, firmware, security or binding changes were made.

Failure categories: A launch/runtime; B absent PnP device; C missing/wrong binding/access denied; D opens but no valid maneuver; E rendering/font/encoder/system runtime. The diagnostics dialog reports these separately where observable.

Sources: Microsoft USB desktop application model https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/windows-desktop-app-for-a-usb-device ; driver signing https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later- ; MIR manual https://www.spirometry.com/media/spirometry-wp-content/uploads/2021/11/EN_winspiroPRO_Manual.pdf .

Input inventory: extracted current spirolab2 source reused. Downloads/spiro.jpg and Downloads/spiro.htm inspected. The specifically named spirolab2(1).zip was not found; Desktop/spirolab2.zip and spirolab.zip exist, and were inspected without extracting over current work. Current source backed up to phase-backups/before-standalone-phase.zip before edits. Root is not a Git repository; no commit or push is possible without creating a new repository (not done).

Acceptance: clean-PC A PENDING, hardware B HARDWARE VALIDATION PENDING. This host cannot be called clean. No accessible clean Windows VM/ISO or physical second PC has been supplied. Existing logs are development fixtures, not verified clinical examinations.
