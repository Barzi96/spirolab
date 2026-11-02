# AGENTS.md

SpiroLab 5.3.1 — standalone Windows Forms spirometry application (.NET Framework 4.x, x86)
that talks to a MIR Spirolab over `MIRUSB.SYS` and prepares a JPG + schema-v2 JSON report.
Persian README and phase evidence live in [README.md](README.md) and [docs/standalone](docs/standalone).

## Git workflow (owner rule)

- Commit locally whenever a change is complete and verified.
- **Never push to GitHub without the owner's explicit approval in the current conversation.**
- The published branch is `main`; push with `git push origin main` after approval.
- Do not rewrite published history, force-push, or change repository visibility.

## Build, test, package

- `app\build.bat` — compiles `app\release\SpiroLab.exe` with the in-box `csc.exe` (no SDK needed).
- `app\test.bat` — builds, then runs all eight check suites (they open real WinForms windows).
- `tools\package-release.ps1` — portable package + self-test + SHA256 into `dist\`.
- Suite logs land in `app\*-test-output\` and are ignored by Git; do not commit them.

## Guardrails

- Never present hardware, clean-PC, BTPS or production HLine validation as done: hardware/clean-PC are PENDING. One owner-authorized live restore write to a designated historical test record passed; this does not establish production or general clinical validation.
- A live clinical HTTP writer exists behind `LiveSubmissionGateway`. It is disabled by default: `MainForm` refuses writes unless a designated test scope (`--hline-test=<NID>:<RECORD>`) or an explicit owner-approved production flag (`--hline-write=all`) is present. Never enable production writes or perform a live write outside the owner-approved designated test record. Production HLine write validation remains PENDING.
- Never add patient data, driver binaries, credentials or tokens to Git; only synthetic fixtures are allowed.
- Version strings and some docs are partly stale (5.2 vs 5.3.1). Keep any edit consistent with the version being released.
