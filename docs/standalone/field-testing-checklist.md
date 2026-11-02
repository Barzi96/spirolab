# SpiroLab 5.3.1 — Field-testing checklist

Use this checklist only on an authorized workstation and an authorized HLine test
record. Do not write to a real patient record until the owner has approved that
specific operation. A field test does not replace independent clinical validation.

## A. Workstation and device

- [ ] Windows 10/11, .NET Framework 4.x, extracted portable folder; no developer tools.
- [ ] `SpiroLab.exe` starts by double-click (no command-line switches) and the window is usable.
- [ ] MIRUSB driver installed/verified; Settings shows the device connected.
- [ ] USB cable and device power are secure; disconnect/reconnect is recognized.
- [ ] A short trial maneuver is acquired from the real MIR Spirolab, not the simulator.
- [ ] Captured flow/volume curves and FVC/FEV1/PEF/FEF25–75 are plausible; no fabricated values.
- [ ] A second real maneuver can be added; stop/finish behavior is correct.
- [ ] Close and reopen the app; the local examination/settings survive a normal restart.

## B. HLine authentication and patient lookup

- [ ] Firefox is signed in to the real HLine site, including MFA where required.
- [ ] SpiroLab shows «متصل به سامانه» by reusing the Firefox session.
- [ ] Sign out/expire the browser session; SpiroLab reports the expiry and does not write.
- [ ] Enter an authorized patient's 10-digit national ID; the exact person and latest
      appropriate medical record are retrieved.
- [ ] Demographics, height and weight populate; missing values stay empty and editable.
- [ ] Manual correction of a demographic field persists without refetching or overwriting.
- [ ] Change the national ID; all previous patient-specific fields, trials, selections,
      exports and submission state are cleared/isolated.

## C. Maneuver selection and report

- [ ] Complete at least three real maneuvers.
- [ ] The automatic primary selection is suitable; click a different eligible row and
      confirm the primary values change to exactly that maneuver.
- [ ] Select up to three report curves independently; the fourth checkbox is refused
      with a clear message, not a silent replacement.
- [ ] Generate a fresh JPG from the current examination; confirm it is not an old backup.
- [ ] Confirm the JPG layout/values match the current selected maneuvers.
- [ ] VEXT follows the clinic rule: highest valid EVol strictly below 200 mL, from
      this examination only; no eligible value produces a clear stop, never zero.
- [ ] Confirm interpretation is not forced to Normal; «Not recorded» remains null.

## D. Normal one-click submission (authorized test record first)

- [ ] Confirm the displayed patient, person ID, medical-record ID, date/status and center.
- [ ] Click «ارسال به سایت» exactly once.
- [ ] Exactly one JPG upload and one spirometry POST occur; no batch or background upload.
- [ ] Success is shown only after numeric, file-association and module-status readback.
- [ ] Open the record in HLine and verify the same values, `test_date`, `file_id` and JPG.
- [ ] Click again; the result is «قبلاً ثبت شده» and no second file/POST is created.
- [ ] Change the current record to one with a different existing result; submission must
      stop with a visible conflict and no overwrite/PATCH/DELETE.
- [ ] Simulate a network timeout after upload/create; the next action must read back the
      server state, not blindly repeat the write.
- [ ] Simulate/observe a 419 CSRF response; verify the app does not report success without
      reconciling an uncertain write.
- [ ] Close the app during/after submission; no unhandled exception and the local exam remains.

## E. Patient A → Patient B isolation

- [ ] Complete and submit Patient A.
- [ ] Register Patient B; confirm no A measurements, selections, exports, JPG or submission
      journal appear in B.
- [ ] Change patients without clicking Send; confirm no automatic or queued submission.
- [ ] Perform and submit B with an explicit click; exactly B's current record is targeted.
- [ ] Return to A's local history; A's result is unchanged and no duplicate was created.

## F. Release and limitations

- [ ] Package SHA-256 matches the delivered value.
- [ ] `Docs/standalone/installation-fa.md` and this checklist are present in the package.
- [ ] Clean-PC test on a second machine: PENDING until actually run.
- [ ] MIR hardware validation at the clinic: PENDING until this checklist is completed
      on real hardware.
- [ ] Independent clinical/measurement validation: PENDING unless a separate report
      comparison has been completed.

## Sign-off

| Item | Result | Tester | Date/time | Notes |
|---|---|---|---|---|
| Workstation/device |  |  |  |  |
| HLine session/patient lookup |  |  |  |  |
| Maneuvers/JPG/VEXT |  |  |  |  |
| Authorized test submission |  |  |  |  |
| Duplicate/conflict/timeout/419 |  |  |  |  |
| A → B isolation |  |  |  |  |
| Clean-PC / hardware / clinical | PENDING |  |  |  |

Do not mark the release production/clinical ready on the basis of mock tests alone.
