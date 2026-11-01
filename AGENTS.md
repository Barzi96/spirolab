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
- `app\test.bat` — builds, then runs all seven check suites (they open real WinForms windows).
- `tools\package-release.ps1` — portable package + self-test + SHA256 into `dist\`.
- Suite logs land in `app\*-test-output\` and are ignored by Git; do not commit them.

## Guardrails

- Never present hardware, clean-PC, BTPS or live-HLine validation as done: they are documented as PENDING.
- No live clinical HTTP writer exists on purpose. `HLineHttpTransport` allows `GET` plus `/auth/login` only.
- Never add patient data, driver binaries, credentials or tokens to Git; only synthetic fixtures are allowed.
- Version strings and some docs are partly stale (5.2 vs 5.3.1). Keep any edit consistent with the version being released.
