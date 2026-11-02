SpiroLab 5.3.1 — GUI end-to-end result (mock HLine)

Date: 2026-11-02 (Asia/Tehran)
Scope: real WinForms `MainForm`, real patient lookup dialog, real report renderer,
real «ارسال به سایت» button, production `SubmissionDraft → LiveSubmissionGateway →
SubmissionCoordinator` pipeline, and a loopback mock HLine server.
No live HLine write was performed in this phase.
Authorization boundary: the released app exposes the normal one-click Send path under
the same `LiveSubmissionGateway` verification; this test used a production-shaped
`MainForm` with a loopback mock HLine server and no test-mode command-line scope.

Maneuver provenance
- Four completed maneuvers were copied from a locally saved MIRUSB-live examination
  (`Simulated=false`, `Imported=false`) on this host.
- Only the flow/volume signals, results and de-identified demographics (age, sex,
  height, weight, origin) were used. Name, national ID, birth date, record identifiers
  and all other patient identity fields were replaced with the mock test patient.
- The resulting fixture stayed outside Git. No real patient name, NID, record ID or
  clinical values are recorded in this document.

GUI path exercised
1. Started the actual compiled `SpiroLab.exe --demo`; confirmed the real window title
   `اسپیرومتری طب کار — SpiroLab 5.3.1` and captured the screen.
2. Opened the actual patient dialog, entered the mock patient national ID, waited for the
   site lookup, verified sex and anthropometry were populated, and saved it.
3. Loaded the four USB-live maneuvers into the actual `MainForm` grid.
4. Clicked the primary column on a deliberately non-best maneuver and three report
   checkboxes through the real `DataGridView.CellClick` handler.
5. Clicked the real «ارسال به سایت» button, not a test-only method.
6. Read the mock server state and the locally rendered export back.

Result: 27/27 GUI E2E checks PASS.
- patient and record identity bound to the verified mock destination
- four `usb_live` maneuvers loaded; no simulation/file replay
- primary values came from the exact clicked maneuver, not the automatic best
- report selection was exactly the three clicked maneuvers
- exactly one multipart upload and one spirometry POST
- `performed_by` was the authenticated mock user
- Gregorian `test_date` matched the report examination date
- `Not recorded` interpretation was sent as null; Normal was not invented
- FVC/FEV1/PEF matched the selected primary maneuver (2-decimal HLine values)
- VEXT matched the clinic rule: highest eligible EVol strictly below 200 mL
- JPG was a newly rendered 2400×3394 image, within 5 MiB
- uploaded JPG was byte-identical to the actual renderer's local output, not the old
  backup JPG
- a repeated click produced no second upload, POST, or report directory
- closing the form during/after the async send produced no unhandled background
  exception (the tooltip/disposal race found by this test was fixed)

Actual screenshots (local evidence, deliberately not committed because the maneuver
signals are from a real device examination):
- `app/gui-e2e-output/screens/00-actual-exe-demo.png` — actual compiled EXE.
- `app/gui-e2e-output/screens/00-normal-startup.png` — actual compiled EXE started by
  double-click/no switches, fully usable window (not `--demo`).
- `app/gui-e2e-output/screens/01-main-before-patient.png`
- `app/gui-e2e-output/screens/02-patient-lookup.png`
- `app/gui-e2e-output/screens/03-maneuvers-loaded.png`
- `app/gui-e2e-output/screens/04-primary-and-report-selected.png`
- `app/gui-e2e-output/screens/05-after-real-send.png`
- `app/gui-e2e-output/screens/06-repeat-click-safe.png`

Safety checks (full list in HLineWriteTests / HLineIntegrationTests)
- identical existing result → `AlreadyPresent`, no upload/POST
- different existing result → hard stop, no overwrite
- lost upload response → read-only file reconciliation, no duplicate upload
- lost create response → `FailedOrUncertain`, no blind retry; next click reconciles
- 419 → fresh CSRF, one rejected-request retry only; uncertain writes are never
  retried automatically
- wrong center / missing file scope / changed destination → refused before upload
- simulated/replayed source cannot be submitted
- no eligible VEXT → null with a visible problem, never zero-filled
- post-save spirometry module status must be complete/saved; `NoData` is a visible
  failure, even when the JSON payload readback matches
- ordinary startup exposes the normal send path with no test scope or production flag;
  the mock integration confirms the current latest record is re-verified before write
- PATCH/DELETE are not reachable from the normal send button

Limits
- The full mock GUI path is not a live GUI-to-HLine test. The earlier live restore
  exercised the production gateway/coordinator and the owner-supplied JPG, not the
  actual report-renderer or button.
- The 419/uncertain-write behaviour is proven against the mock, not against a real
  transactional server timeout.
- Clinical measurement validation against an independent device report remains
  PENDING. MIRUSB hardware, clean-PC and BTPS validation remain PENDING.
- Therefore SpiroLab 5.3.1 is not declared production/clinical ready.
