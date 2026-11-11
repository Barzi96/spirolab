# AGENTS.md

SpiroLab 5.8.4 — standalone Windows Forms spirometry application (.NET Framework 4.x, x86)
that talks to a MIR Spirolab over `MIRUSB.SYS` and prepares a JPG + schema-v2 JSON report.
Persian README and phase evidence live in [README.md](README.md), [docs/operator](docs/operator) and [docs/standalone](docs/standalone).

## Git workflow (owner rule)

- Commit locally whenever a change is complete and verified.
- **Never push to GitHub without the owner's explicit approval in the current conversation.**
- The published branch is `main`; push with `git push origin main` after approval.
- Do not rewrite published history, force-push, or change repository visibility.

## Build, test, package

- `app\build.bat` — compiles `app\release\SpiroLab.exe` with the in-box `csc.exe` (no SDK needed); no simulation or test route is compiled into it.
- `app\build.bat test` — compiles the isolated `app\release-test\SpiroLab.Test.dll` with `SPIROLAB_TEST` for automated tests only.
- `app\test.bat` — builds both, then runs all eleven check suites (they open real WinForms windows). The reference `5721f69` has **1117 PASS + 10 SKIPPED** without the optional external MIR capture. This branch adds automatic SDA identification and the 4.8 LAST layout from the owner's supplied project, preserving the existing 8.2 path and HLine logic. Suites use isolated run subfolders. Hardware validation of 5.8.4 on both devices, live HLine, real-browser/sleep and clean-PC remain PENDING. See `docs/operator/dual-firmware-584-fa.md` and the historical MIR evidence.
- 5.8.4 software checks: **1157 PASS / 0 FAIL / 10 SKIPPED**; independent MIR acceptance **191 PASS**. These include both synthetic SDA/LAST layouts, device switching, XML/snapshot round-trip and provenance rejection. They are not physical-device validation.
- Operator docs live in [docs/operator](docs/operator); historical GUI evidence is [docs/standalone/gui-e2e-results.md](docs/standalone/gui-e2e-results.md) (not a live write).
- `tools\package-release.ps1` — portable package + self-test + SHA256 into `dist\`.
- Suite logs land in `app\*-test-output\` and are ignored by Git; do not commit them.

## Guardrails

- Never present hardware, clean-PC, BTPS or production HLine validation as done: hardware/clean-PC are PENDING. One owner-authorized live restore write to a designated historical test record passed; this does not establish production or general clinical validation.
- The live clinical HTTP writer behind `LiveSubmissionGateway` is enabled for the normal operator workflow: double-clicking the app exposes the current patient's re-verified latest record and the one-click Send action. Do not perform a live write to a real patient without the owner's explicit authorization; production HLine write validation for arbitrary patients remains PENDING. Automated tests may inject mock gateways and designated test records, but the released app has no test-mode switch or test-record dependency.
- Never add patient data, driver binaries, credentials or tokens to Git; only synthetic fixtures are allowed.
- New examinations live in `records/<Persian-day>/` beside the executable, with stable creation-time names. Legacy AppData examinations are copied only on explicit operator action; never delete or silently migrate originals. Settings remain in AppData. Never bundle records or test-output folders.
- The owner-requested release identifier on this branch is 5.8.4. Historical documents retain their own prior versions. Every new HLine write must pass `DeviceProvenanceValidator` over the frozen snapshot using its persisted wire layout; do not add a production bypass flag.
