# AGENTS.md

SpiroLab 5.4.6 — standalone Windows Forms spirometry application (.NET Framework 4.x, x86)
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
- `app\test.bat` — builds both, then runs all eleven check suites (they open real WinForms windows). Current total: **964 PASS + 10 SKIPPED** without the optional external MIR capture; the real-capture checks run only when its path is supplied as the first argument. The separate opt-in pending-end acceptance passes. Two user-operated manual finishes passed on the connected MIR; broader hardware validation remains PENDING. See `docs/standalone/manual-finish-field-validation-546.md`.
- Operator docs live in [docs/operator](docs/operator); historical GUI evidence is [docs/standalone/gui-e2e-results.md](docs/standalone/gui-e2e-results.md) (not a live write).
- `tools\package-release.ps1` — portable package + self-test + SHA256 into `dist\`.
- Suite logs land in `app\*-test-output\` and are ignored by Git; do not commit them.

## Guardrails

- Never present hardware, clean-PC, BTPS or production HLine validation as done: hardware/clean-PC are PENDING. One owner-authorized live restore write to a designated historical test record passed; this does not establish production or general clinical validation.
- The live clinical HTTP writer behind `LiveSubmissionGateway` is enabled for the normal operator workflow: double-clicking the app exposes the current patient's re-verified latest record and the one-click Send action. Do not perform a live write to a real patient without the owner's explicit authorization; production HLine write validation for arbitrary patients remains PENDING. Automated tests may inject mock gateways and designated test records, but the released app has no test-mode switch or test-record dependency.
- Never add patient data, driver binaries, credentials or tokens to Git; only synthetic fixtures are allowed.
- The production release identifier is 5.4.6. Historical documents may describe their own prior versions; keep new user-facing edits consistent with 5.4.6. Every new HLine write must pass `DeviceProvenanceValidator` over the frozen snapshot; do not add a production bypass flag.
