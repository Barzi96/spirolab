# Clean Windows acceptance record and procedure

## Actual execution status

- Development OS: Windows 10 x64 19045, previously had MIR software: NOT CLEAN.
- Clean Windows A (launch, form, preview, JPG+JSON, reopen): **PENDING**.
- Clean Windows B (actual USB driver + live maneuver): **HARDWARE VALIDATION PENDING**.
- x86 OS deployment: PENDING.
- Current Windows Sandbox binary absent, feature Containers-DisposableClientVM Disabled. No feature, security or driver changes made to create a test environment.
- Private driver package exported and signature/hash verified; second-PC installation/binding remains PENDING. Portable EXE has no driver binaries.

## Reproducible acceptance on a provided clean PC/VM

1. Record Windows edition/build/architecture, base-image provenance and confirm MIR software has never been installed. Do not use this developer machine as proof.
2. Extract the portable ZIP as standard user. No compiler, sources, MIR EXE, activation or database should be present.
3. Run `SpiroLab.exe --self-test <writable folder>`. Record exit code and selftest-result.txt. Reopen the generated JPG in a standard image viewer and JSON in a text editor. Confirm same patient, saved selected date, separate capture timestamp, IDs, values, source/status and null VEXT in the default self-test.
4. Run `SpiroLab.exe --demo` explicitly. Inspect the patient form and report preview. Export JPG+JSON, restart, reopen the local record. Confirm NON-CLINICAL TEST DATA on image and JSON. Do not use image sample patient data as defaults.
5. Disconnect/no driver: inspect diagnostics and prove no simulated fallback when starting a real test.
6. On a physical clean PC, attach the exact supported device with adequate power. Install the separately exported private signed MIRUSB package using driver-installation.md. Do not install MIR desktop applications. Capture driver version/signer/architecture and actual binding.
7. Perform a real supported live maneuver. Record deidentified packet types, timing, completion, cancellation and disconnection behavior; compare against an independently verified device report. The existence of VID/PID or a response to State() is insufficient.
8. Exercise absent driver, wrong binding, denied access, read timeout, no samples, cancel and completion. Verify no reset/flash/configuration commands. Test native cancel acknowledgement with actual driver.
9. Mark A/B PASS only when the corresponding evidence exists. A passing mock or replay must not mark B PASS.

## Native cancellation limit

Control waits are bounded at 2 s; bulk operations use their 2–3 s configured timeout, followed by at most 2 s cancellation acknowledgement. Native buffers are not freed while the kernel owns them. A driver that fails to acknowledge cancellation is quarantined and reconnect disabled for that transport instance; timers defer release until completion. This protects memory and returns control without blocking a managed worker indefinitely. A permanently unresponsive kernel driver cannot be forcibly repaired by this application. That exceptional path can retain pinned memory until OS completion/process exit and requires real-driver testing. It is NOT claimed to meet the strongest no-pending-operation guarantee under arbitrary broken-driver behavior. Restart/recovery must follow vendor support guidance; do not reset firmware or rebind blindly.
