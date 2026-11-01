# Software test record — 2026-11-01

## Passed

- 75 existing regression checks retained. Eight predicted reference values match the supplied photograph after display rounding. The archived anonymous capture is an unverified development replay fixture, not a verified clinical examination.
- 63 standalone software checks: runtime/native import allowlist, WOW64 driver path, missing/wrong-driver categories, no simulated USB fallback, no website transport implementation, snapshot consistency and isolation from source edits, chronology/IDs, ratio units, operator/manual interpretation, null VEXT and separate estimated BEV, exact two-file export, real JPEG markers/codec/dimensions/DPI, JSON v2 fields, unique export directories, failure rollback, existing-report preservation, v1 local-record migration and ID persistence.
- Mock USB completion/cancel/failure tests retained: natural end does not send ESC; abort does not read stale results; failed reads close the session.
- Structural native timeout check: bounded overlapped control/bulk paths and deferred cleanup present; this is NOT proof that the actual kernel driver acknowledges cancellation correctly.
- Source audit: only kernel32.dll/setupapi.dll native imports, .NET system managed references; no active vendor DLL/EXE/database/activation dependency. No firmware/reset/flash/calibration commands added.

## Pending, not passed

- Clean Windows 10 A: actual never-MIR-installed PC/VM, launch, form, preview, export, restart.
- Hardware B: HARDWARE VALIDATION PENDING, actual live maneuver and independent measurement comparison.
- Authorized standalone signed-driver package and license; driver installation/recovery on clean PC.
- x86 OS, real USB permissions/binding, driver timeout/cancel acknowledgement under actual hardware failure.
- Clinical validation: timing/time-zero, back extrapolation, BTPS, algorithmic interpretation and quality grading.

## Failed automated software checks

None in the final run. One compiler warning deliberately exercises the deprecated CurvesPath compatibility alias; the primary export has exactly JPG and JSON and does not deliver an extra curves image.

## Evidence

Full command output: app/standalone-test-output/full-test-results.txt. Source tests remain under app/Tests. The final portable-only self-test result is added to each packaged release; it describes this host and explicitly does not claim clean-PC/hardware acceptance.
