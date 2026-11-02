# MIR refinement regression report — 2026-11-02

Branch: `mir` only. Stable `main` remains `87188b766eddb4f0ee68fb0ebdd3783b7dde06fe`; the pre-refinement `mir` head was `50a36a6cfb73aa208b86ca3e10ac99039e865906` (tag `stable-mir-2026-11-02`). No `main` edit, merge, reset, rebase, force-push or branch deletion was performed.

## Result

`app\test.bat` completed all ten suites on this host:

| Suite | Checks |
|---|---:|
| RegressionTests | 76 |
| StandaloneTests | 73 |
| PrecisionTests | 66 |
| ManeuverSelectionTests | 61 |
| ClinicalUiTests | 161 |
| MirBestValuesTests | 101 |
| RefinementTests (new) | 41 |
| HLinePhaseTests | 25 |
| HLineIntegrationTests | 169 |
| HLineWriteTests | 19 |
| **Total** | **792** |

Previous verified baseline was 744 (nine suites); the refinement adds 48 checks and one suite. All listed checks passed in the final run. Intentional expectation changes were limited to workflow version 4, selected-three scope, device-core rejection and the compact first column.

## What the new/changed checks cover

- Workflow-4 automatic top-three by quality tier then `FVC+FEV1`, deterministic acquisition tie-break.
- Manual final selection, three-item limit, persistence, no silent replacement by a later acquisition, automatic reset.
- Selected aggregate: independent maximum FVC/FEV1, ratio, independent PEF, FEF25–75 from the selected largest `FVC+FEV1` maneuver, exact source IDs.
- Non-blocking warning when an unselected eligible maneuver has higher FVC/FEV1.
- One snapshot for UI/JPG/JSON/HLine draft; recomputation after selection change; no stale selected values.
- Delete by stable ID, archive/restart, selection cleanup, aggregate recomputation, undo, VEXT exclusion, failed-capture separation.
- Failed/unassociated/truncated/invalid device result never enters the active table; exact Persian retry message.
- All four required device core fields; new real captures are marked device-core-required and cannot use software FVC/FEV1/PEF/FET in the clinical path; requirement survives reload.
- HLine journal/recheck safety: no second upload or POST, no designated test record path, uncertain journal reconciliation, read-only repeat, cross-patient rejection.
- Local timing diagnostics contain generic stages and no patient/record/payload identifiers.

## Deliberately not tested / not claimed

- Real MIR hardware capture parity, firmware equivalence, BTPS or cross-process maneuver identity.
- Clean second-PC/Windows acceptance.
- A new live HLine production write; only mock/fixture submission paths and the prior owner-authorized historical restore evidence exist.
- Production network bottleneck measurement or performance tuning. Stage timings are local measurements/instrumentation, not a production latency benchmark.
- Clinical validation or ATS/ERS adjudication of the selected set.

See [mir-selected-three-531.md](mir-selected-three-531.md) for the contract, deletion rules, timing log instructions and limitations.
