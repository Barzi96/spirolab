# Delivery report — independent SpiroLab 5

## A. Current application audit

Existing codebase retained: Windows Forms / .NET Framework 4.x / x86, direct Windows SetupAPI + vendor USB IOCTL. Single-screen patient/test workflow retained, any number of maneuvers up to the existing safety limit, local history and best-value selection. Center/operator and manually entered interpretation added. Runtime references are Windows assemblies and kernel32/setupapi only. See dependency-audit.md.

## B. Root cause of MIR software dependency

Proven: active transport needs the MIRUSB driver contract. Host has MIRUSB 2.0.0.9 (x64, M.I.R. srl); local Authenticode inspection reported Valid, Microsoft Windows Hardware Compatibility Publisher. Likely: installing MIR software supplied this driver on the second PC. Unknown: which A–E failure actually occurred there, driver compatibility and authorization to distribute a standalone driver. No claim of a fully proven root cause for that PC.

## C. Standalone architecture

Portable EXE + .NET runtime config; no vendor desktop EXEs, DLLs, databases, activation or application registry reads. OS .NET Framework 4.x, GDI+/fonts, WMI and system native DLLs remain. Device operations still require a matching signed/authorized MIRUSB driver. WinUSB is not substituted. No proprietary binaries are bundled and no binding changes are performed.

## D. Clean Windows deployment

Portable-only self-test passed on the existing development Windows 10 x64 host. This is not a clean-PC test. Sandbox is disabled/absent; no clean VM/ISO or second PC was supplied. Clean Windows Test A is PENDING; x86 OS acceptance is PENDING. See acceptance.md for the exact reproducible procedure.

## E. USB integration

Only evidence-supported ON/state/FVC/packet/LAST/ESC operations remain; no archive download/reset/flash/configuration/calibration additions. Control IOCTLs now use valid OVERLAPPED state; operation/cancel waits are bounded. Native buffers are kept alive until completion if a broken driver does not acknowledge cancellation; managed workers return, but exceptional native cleanup can remain pending. The strongest no-pending-operation requirement is not claimed for an unresponsive kernel driver. Existing mock cancel/end/read-failure tests pass. Actual driver behavior and live measurements: HARDWARE VALIDATION PENDING.

## F. JPG report

Examples/Spirometry-Report.jpg is a real 97-quality JPEG, 2400×3394, 300 DPI with white portrait layout. It follows the reference's header, combined axes, monochrome PRE line styles, BEST VALUES and parameter tables, interpretation/quality/repeatability and independent footer. Persian names and LTR numbers are rendered separately. There are no stamps or copied patient data. No unsupported predicted curve or verified-looking VEXT/quality grade is invented. See report-comparison.md and the same-snapshot example JSON.

## G. JSON schema

schemaVersion=2 groups: patient, examination, device, spirometry, maneuvers, interpretation, quality, report, provenance. FVC/FEV1/PEF independent maxima, FEV1_FVC percentage, FEF25_75 from best sum maneuver, VEXT null. Full precision, string IDs, original maneuver IDs/indices, first acquisition timestamp, operator source and per-field verification/missing status retained. Details in field-mapping.md.

## H. Data accuracy

All reconstructed measurements, time-zero/BTPS, quality and algorithmic clinical interpretation remain unvalidated. Estimated BEV is preserved separately, never silently mapped to VEXT. Predicted scalar values use the existing ERS formulas; matching the eight reference-photo values does not validate clinical measurements. Manual interpretation is distinguishable but not independently authenticated. No simulated/replayed output is presented as a clinical examination.

## I. Tests

Passed: 75 retained regressions + 63 new software checks = 138. Packaged executable-only self-test additionally passed on the development host, including persistence and reopening JPG/JSON. Failed final automated software checks: none. Pending: clean Windows A, real hardware B, authorized standalone driver installation, actual native cancellation behavior, x86 OS and clinical validation. Full output in full-test-results.txt; summary in test-results.md.

## J. Git and changed files

Project root has no Git repository. Commit hash: N/A. No repository initialization, commit, push or destructive command was performed. Existing work preserved in phase-backups/before-standalone-phase.zip. changed-files.txt lists modified/added paths. Source ZIP contains current sources/tests/docs and an anonymous unverified development fixture needed by retained regressions; portable ZIP contains no source or compiler.

## K. Scope confirmation

MIR Spiro desktop application: not referenced/required by code. WinspiroPRO desktop application: not referenced/required by code. Remaining MIRUSB hardware driver prerequisite: documented, authorization/clean installation unresolved. JPG and JSON: implemented from one frozen snapshot. Website login/API/browser/upload: absent. Fabricated medical measurements: none; synthetic examples clearly NON-CLINICAL TEST DATA. No claims of unverified hardware compatibility. This phase stops here.
