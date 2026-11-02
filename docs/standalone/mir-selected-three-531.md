# MIR selected-three refinement — SpiroLab 5.3.1 (`mir` branch)

This note describes the workflow-4 clinical selection contract implemented on the isolated `mir` branch. It does not replace the historical v3 contract, does not claim MIR firmware equivalence or BTPS/clinical validation, and does not claim hardware validation.

## 1. Clinical acquisition

- A new real USB examination accepts FVC, FEV1, PEF and FET only from a device-reported `cod_LAST` block that is bound to the completed capture by capture token, serial (when available), immediate single read, observed ranges, non-duplicate raw block and non-truncation.
- If any of those four required core fields is missing or invalid, the attempt is not added to the active maneuver table. The software analyzer is not substituted in the clinical path. The attempt is kept only in separate local diagnostic history (`FailedCaptures`) and the UI shows:
  `نتیجه مانور از دستگاه دریافت نشد؛ لطفاً مانور را تکرار کنید.`
- Reconstructed per-maneuver values remain available as separate `softwareReconstruction` diagnostics; they are not relabelled as device values.
- Packet parsing and observed offsets/scaling are unchanged. The protocol has no maneuver ID; cross-process identity remains unproven and is reported as a limitation.

## 2. Selected set is the clinical set (workflow 4)

`SelectionWorkflowVersion = 4`. There is no primary radio. The first column keeps only the maneuver number and a compact «حذف» action; the rightmost compact control is labelled «انتخاب نهایی» with:

> حداکثر سه مانور؛ بهترین مقادیر گزارش و سایت از همین مانورها انتخاب می‌شوند.

- Automatic recommendation: quality tier, then `FVC + FEV1`, deterministic acquisition-sequence tie-break, maximum three. It is re-evaluated when a new valid maneuver is added, unless the operator has manually overridden the set.
- Manual override: check/uncheck up to three eligible maneuvers. The override and selected IDs are persisted. A later acquisition must not silently replace it.
- «بازگشت به انتخاب خودکار» resets the manual flag and restores the current recommendation.
- If an unselected eligible maneuver has a higher FVC or FEV1 than the selected best, a short non-blocking warning is shown in the summary line and recorded in JSON. It never blocks finalization.

The one authoritative aggregate over the selected set is used for the UI summary, JPG, JSON, HLine numeric payload and readback:

- largest acceptable FVC;
- independent largest acceptable FEV1;
- `best FEV1 / best FVC × 100`;
- independent largest eligible PEF;
- FEF25–75 from the selected eligible maneuver with the largest `FVC + FEV1` (not the largest FEF);
- exact source maneuver IDs and existing quality-limited usable fallback.

Selection changes recompute the snapshot; no stale values are retained. A successful or uncertain HLine submission cannot be silently changed or resent.

## 3. Delete, archive and recovery

- Delete is by stable maneuver ID.
- The maneuver is removed from the active collection, final selection, aggregate and clinic VEXT calculation, then UI/snapshot/save are refreshed.
- It is moved to `DiscardedTrials` with `DiscardedAt` and `SelectedWhenDiscarded`; it is not erased from local persistence.
- `بازگرداندن آخرین حذف` (toolbar/context menu or Ctrl+Z) restores the original ID, acquisition sequence and, where possible, its prior final-selection membership.
- Discarded evidence survives XML restart and remains outside the active clinical view. Finalized examinations cannot be deleted or changed.
- `ClinicVext.Select()` is unchanged and receives only the filtered active real maneuvers. Deleted maneuvers cannot supply VEXT. Examples remain `[110,132,105] → 132`, `[230,185,160] → 185`, `[200,201,0] → unavailable`.

## 4. HLine safety and timing

- One deliberate send only; exact JPG bytes and numeric fields from one immutable selected snapshot.
- Existing journal/conflict/idempotency behavior is preserved, including read-only `SubmissionCoordinator.Recheck()` reconciliation with or without a designated test record.
- No blind retry, automatic PATCH/DELETE, batch send, overwrite or cross-patient reconciliation.
- Low-risk timing diagnostics are written only to:
  `%LOCALAPPDATA%\SpiroLab\logs\hline-timing.log`
- The log contains generic stage names, milliseconds, operation name and outcome only. It never contains national ID, patient name, birth date, record ID, cookies, CSRF token, payloads, JPG bytes or raw device data.
- Stages instrumented in the send path include: `snapshot_build`, `local_export`, `local_persist`, `draft_build`, `draft_validate`, `designated_record_verify`, `destination_verify`, `existing_result_read`, `server_result_read`, `jpg_download`, `jpg_upload`, `jpg_upload_request`, `upload_reconcile`, `create_post`, `create_post_request`, `server_readback`, `module_verify`, `module_status_poll` and `journal_persist`. Some stages are nested; read them as a breakdown, not an additive budget.

### How to read timing

1. Perform one normal submission through the UI.
2. Open the log path above in Notepad.
3. Compare the `operation=send` line's stage totals. Stages are nested where the gateway records a more specific request internally; do not sum all stages as if they were additive.
4. These are local measured timings for the operator's machine and HLine response. Mock/test timings are not production performance evidence.
5. No third-party telemetry is used.

## 5. Compatibility and limitations

- New behavior applies only to workflow-4 examinations created by this build.
- Existing v3 final/submitted records remain readable and are not silently re-finalized or reinterpreted.
- Old XML fields remain for historical reading; `ReportManeuverIds`, `ReportSelectionManual` and `SelectionWorkflowVersion` round-trip.
- Hardware validation remains **PENDING**; a live USB capture was observed for development, but not general MIR parity.
- BTPS correction, clinical validation, clean-PC validation and general production HLine write validation remain **PENDING**.
- The protocol does not expose a maneuver identifier. Capture-token/serial/immediate-read/duplicate/truncation checks are the best available local association, not proof of cross-process identity.
- The app is x86 WinForms and uses the in-box .NET Framework 4.x compiler; no SDK is required.
