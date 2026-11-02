# MIR measurements and session BEST VALUES — SpiroLab 5.3.1

This document is the current selection/JSON contract. It supersedes the primary-radio instructions in the historical 5.1/5.2 addenda. It does not claim MIR firmware equivalence, BTPS correction, clinical validation or clean-PC validation.

## 1. Measurement provenance

Every per-maneuver number carries one of these sources:

| Source code | Meaning |
|---|---|
| `device_reported` | A result block received after the exact completed USB capture, with matching session capture sequence, matching device serial (when available), valid observed ranges and a non-duplicate raw block. |
| `software_calculated` | Reconstructed from samples recorded by this application. Not a device/MIR result. |
| `legacy_unverified` | A persisted older device block that cannot be tied to the current capture. Retained for review only. |
| `derived_from_selected_measurements` | FEV1/FVC derived from the selected best FEV1 and best FVC. |
| `unavailable` | No numeric value. Zero is never used as a substitute. |

The local JSON keeps `measurements` (the values actually selected for the session) and `softwareReconstruction` separately. A verified device value can therefore sit beside the reconstructed value for diagnostics without relabelling either one. The device quality byte is exposed only as `qualityFieldUnverified`; it is not interpreted as an ATS grade.

### Capture association

`DeviceSession` increments a process-local capture sequence at each start. A result is accepted only when:

- the maneuver reached the device's natural completion packet (`0x04`), not ESC/user stop;
- the result is read immediately for that capture token (single read, delay at most 10 s);
- the block is at least the observed result prefix and is not truncated;
- the block is not byte-identical to the previous accepted block;
- the serial number and observed value ranges match.

The protocol has no documented maneuver identifier. A block from another process/patient that is otherwise well-formed cannot be distinguished with certainty; this remains a hardware/protocol limitation, not a claim of solved cross-session identity.

Observed `cod_LAST` fields used by this build (unsigned 16-bit, 0.01 scaling): FVC at `0x10`, FEV1 at `0x12`, PEF at `0x14`, FET at `0x1A`, PEF time at `0x50`. The quality byte at `0x0A` is retained but not graded. These offsets are observation-backed, not a published MIR contract.

## 2. Session aggregate

`SessionBestValues` is the single authoritative aggregate for UI, JPG, JSON and HLine:

- **Best FVC**: largest acceptable FVC among eligible maneuvers.
- **Best FEV1**: largest acceptable FEV1, independently of the FVC source.
- **FEV1/FVC**: `Best FEV1 / Best FVC × 100`, full precision internally.
- **Best PEF**: largest valid PEF, independently.
- **Best FET**: largest valid FET, independently.
- **FEF25–75**: from the eligible maneuver with the largest `FVC + FEV1`; explicitly **not** the largest FEF25–75 value.
- Predictions are ERS/ECCS 1993. Percent predicted is `value / matching predicted × 100`.
- Eligibility excludes incomplete, technical-failure, operator-rejected and unusable maneuvers. FVC and FEV1 have separate usability/acceptability decisions.
- If no quality-acceptable maneuver exists, a usable value may be reported with an explicit `qualityLimited` flag and limitation list; it is never silently presented as fully acceptable.

Non-finalized report selection does not change this aggregate. `ExamSummary.Trials` is only the displayed subset.

## 3. Report curves

- Up to three real report curves are chosen automatically by quality tier, then `FVC + FEV1`, with deterministic acquisition-sequence tie-break.
- The operator may check/uncheck any eligible real curve. This changes only the displayed curves and per-maneuver table columns. It does not change BEST VALUES, the derived ratio, HLine fields or VEXT.
- The uploaded JPG and JSON describe the same immutable snapshot. A manually chosen subset is still validated against eligible maneuvers.

## 4. HLine mapping

| HLine field | Source |
|---|---|
| `fvc_amount` | Best FVC |
| `fev1_amount` | Best FEV1 |
| `pef_amount` | Best PEF |
| `fef2575_amount` | FEF25–75 from the largest `FVC + FEV1` maneuver |
| `fvc_percent` | Best FVC / predicted FVC × 100 |
| `fev1_percent` | Best FEV1 / predicted FEV1 × 100 |
| `pef_percent` | Best PEF / predicted PEF × 100 |
| `fef2575_percent` | selected FEF25–75 / predicted FEF25–75 × 100 |
| `fev1_fvc_percent` | Best FEV1 / Best FVC × 100 |
| `vext` | **Unchanged** `ClinicVext.Select()` rule |

`ClinicVext.Select()` considers only real live maneuvers and accepts only `0 < EVol < 200 mL`, chooses the largest, and returns unavailable if none. Examples: `[110,132,105] → 132`, `[230,185,160] → 185`, `[200,201,0] → unavailable`. Manual local VEXT review is separate and never becomes the HLine `vext` field.

## 5. JSON compatibility

Workflow 3 (`SelectionWorkflowVersion = 3`, `PrimarySelectionRequired = false`) removes the single primary maneuver. Old XML fields (`OperatorSelectedManeuverId`, `SelectionMethod`, `SelectionOperatorName`, `SelectionReason`, `SelectionTimestamp`, `PrimarySelectionRequired`) remain readable for historical records but are not required for new examinations and are marked `not_applicable_workflow3_best_values_aggregate`.

For workflow 3:

- `bestValues` is the aggregate over all eligible maneuvers, not the report subset.
- `bestReportedValues` (and the compatibility alias `spirometry`) mirror that aggregate. `spirometry.VEXT` remains the local legacy/manual value, not the HLine field.
- `vextSelection` records the unchanged clinic EVol rule and its source.
- `siteExport` is explicitly `retired_single_site_maneuver_use_bestValues` with no measurements.
- Each maneuver has `measurements`, `softwareReconstruction`, `deviceResult`, `includedInBestReportedValues` and quality/assessment fields.

## 6. Evidence status

**Automated tests:** the nine suites currently total **744 checks**, including anonymized/synthetic MIR reference cases A–D, capture-token mismatch, duplicate/truncated block rejection, independent maxima, report-selection independence, JSON/JPG/HLine agreement and VEXT examples. See `app\test.bat` and `app\*-test-output\`.

**Hardware validated:** **NO.** A live USB capture was observed and its `cod_LAST` block was decoded for development, and one owner-authorized HLine restore to a designated historical test record passed. Neither establishes general hardware parity, firmware algorithm equivalence, BTPS accuracy, clean-PC behaviour or production HLine safety. Those remain **PENDING**.
