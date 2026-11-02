> Current version 5.3.1: use [best-values-531.md](best-values-531.md) for the authoritative aggregate and workflow-3 schema. The 5.1/5.2 text below is retained as history; the primary radio has been removed.

# Local field mapping — schemaVersion2, application 5.3.1

The supplied local spiro.htm was inspected as a visible results reference only. No login, HTTP/API client, credentials, browser automation or upload was implemented.

| Field | JSON location | Unit and selection |
|---|---|---|
| FVC | bestValues.fvc / spirometry.FVC | L; largest acceptable session FVC; device source only when capture bound |
| FEV1 | bestValues.fev1 / spirometry.FEV1 | L; independent largest acceptable FEV1 |
| FEV1/FVC | bestValues.fev1Fvc / spirometry.FEV1_FVC | percent; Best FEV1 / Best FVC × 100, full precision internally |
| FEF25–75 | bestValues.fef2575 / spirometry.FEF25_75 | L/s; largest FVC+FEV1 eligible maneuver, not largest FEF |
| PEF | bestValues.pef / spirometry.PEF | L/s; independent largest valid PEF |
| VEXT (HLine `vext`) | vextSelection | mL; unchanged highest real EVol strictly between 0 and 200 mL; sourceManeuverId and rule recorded |
| VEXT (local manual) | spirometry.VEXT | mL; legacy/manual local review value or null; never the HLine field |
| Device result | maneuvers[].deviceResult / measurements | Capture-bound reported values, raw block retained locally; unverified against firmware |
| Software reconstruction | maneuvers[].softwareReconstruction | Reconstructed values kept separately for diagnostics, never relabelled as device |
| Selected date | examination.examinationDate / testedAt | Saved ISO date / ISO date at midnight; authoritative for JPG |
| Actual acquisition | examination.captureStartedAt / maneuvers[].testedAt | Original first capture / per-maneuver timestamps |
| Date provenance | examination.dateSource | windows_local_date, operator_selected_date, legacy_capture_date |
| Center/operator | examination.centerName / operatorName | Copied into exam; no later global-default substitution |
| Reviewer | review.manualVext | Name, source, reviewed flag, explicit manual selection method |
| Finalization | review.finalizedAt | Actual finalization timestamp; independent of selected exam date |
| Interpretation | interpretation | Clinician text with source, or unvalidated independent algorithm |
| Patient ID | patient.nationalId | String, leading zeros retained |
| IDs | examination.id / snapshotId; maneuvers[].id | Stable examination, finalized snapshot and maneuver IDs |

Every measurement retains value, unit, predicted, percentPredicted, valueSource, verificationStatus and missingDataStatus. No intermediate numeric rounding is introduced. Display rounds volumes/flows to2 decimals, ratios1, percent predicted0 and EVol mL0. Unsupported reference range/group gives null predictions.

Per-maneuver estimatedBEV is separate from VEXT. backExtrapolation records Time Zero, method, source adequacy and unverified device parity. Mathematically available BEV uses V(t0) after peak-tangent intersection. Missing/unbracketed input gives null and a reason; no zero is substituted. The HLine VEXT field follows the clinic convention `max(0 < EVol < 200 mL)` over real maneuvers and records its source maneuver and calculation provenance in `vextSelection`; EVol timing stays reconstructed and is not paired-MIR verified. The manual VEXT review value remains a local report record and is also reflected in JPG.

All maneuvers remain in acquisition order in JSON. Up to three real, signal-usable maneuvers selected by quality and then FVC+FEV1 are drawn, in their original order with actual PRE indices. Operator checkbox changes affect only that display set. One eligible maneuver is sufficient for development export. Signal usability is not clinical acceptability.

Snapshot deep-copies signals/analysis/references; JPG and JSON use that same copy. Finalization persists cloned analysis and stable snapshot UUID. Reopening final records reuses frozen results, including BEV, rather than recalculating with future formulas. Metadata/capture is locked in the UI after finalization. XML saves retain .bak; published export folders are never overwritten. Each saved draft attempt now retains its analysis too; only legacy records without a stored analysis reconstruct signals. Missing legacy selected date derives from first saved capture; absent old operator/center remain absent.

Source simulation/replay is NON-CLINICAL TEST DATA. Live reconstruction is DEVELOPMENT — UNVERIFIED MEASUREMENTS. Clinical/hardware/BTPS validation remains pending. Device interpretation blocks are not treated as verified result sources. No occupational exposure, smoking or website-only fields are invented.

## 5.3.1 workflow-3 selection
`SelectionWorkflowVersion=3` removes the single primary maneuver. `bestValues` is the independent aggregate over all eligible maneuvers; `bestReportedValues` and `spirometry` mirror it for compatibility. The report display subset is independent and cannot change the aggregate. Legacy primary fields are read for historical records and are not required for new examinations. See [best-values-531.md](best-values-531.md).
