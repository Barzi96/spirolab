# Local field mapping — schemaVersion2, application5.1

The supplied local spiro.htm was inspected as a visible results reference only. No login, HTTP/API client, credentials, browser automation or upload was implemented.

| Field | JSON location | Unit and selection |
|---|---|---|
| FVC | spirometry.FVC | L; largest complete usable maneuver value; source UUID |
| FEV1 | spirometry.FEV1 | L; independent maximum; source UUID |
| FEV1/FVC | spirometry.FEV1_FVC | percent; maxFEV1/maxFVC*100, firmware equivalence pending |
| FEF25–75 | spirometry.FEF25_75 | L/s; largest FVC+FEV1 maneuver, not largest FEF |
| PEF | spirometry.PEF | L/s; independent maximum; source UUID |
| VEXT | spirometry.VEXT | null when unavailable; explicitly reviewed manual value in mL otherwise |
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

Per-maneuver estimatedBEV is separate from VEXT. backExtrapolation records Time Zero, method, source adequacy and unverified device parity. Mathematically available BEV uses V(t0) after peak-tangent intersection. Missing/unbracketed input gives null and a reason; no zero is substituted. VEXT manual source is also reflected in JPG. No automatic maximum-EVol rule is inferred from B's132mL match.

All maneuvers remain in acquisition order in JSON. At most three complete signal-usable maneuvers selected by FVC+FEV1 are drawn, in their original order with actual PRE indices. One maneuver is sufficient for development export. Signal usability is not clinical acceptability.

Snapshot deep-copies signals/analysis/references; JPG and JSON use that same copy. Finalization persists cloned analysis and stable snapshot UUID. Reopening final records reuses frozen results, including BEV, rather than recalculating with future formulas. Metadata/capture is locked in the UI after finalization. XML saves retain .bak; published export folders are never overwritten. Each saved draft attempt now retains its analysis too; only legacy records without a stored analysis reconstruct signals. Missing legacy selected date derives from first saved capture; absent old operator/center remain absent.

Source simulation/replay is NON-CLINICAL TEST DATA. Live reconstruction is DEVELOPMENT — UNVERIFIED MEASUREMENTS. Clinical/hardware/BTPS validation remains pending. Device interpretation blocks are not treated as verified result sources. No occupational exposure, smoking or website-only fields are invented.

## 5.1.1 primary selection addendum
primaryManeuverSelection stores explicit ID/sequence/method/time/operator/reason; bestReportedValues aliases the unchanged aggregate spirometry mapping. report identifies and includes the chosen primary among at most three display curves. All attempts, rejection/validity/quality/provenance remain in maneuvers and local raw history. No count cap. See [maneuver-selection-addendum.md](maneuver-selection-addendum.md).
