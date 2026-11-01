# MIR reference parity matrix — 5.1

This is a comparison of evidence layers. Matching printed output fixtures is not proof of a matching acquisition pipeline. Both photos and the local HTML were inspected; no raw paired acquisition for A/B is available. Identity, operator identity, stamps and dates from HTML were not copied into fixtures.

| Layer | Current behavior / evidence | Classification and actual remaining gap |
|---|---|---|
| 1 Raw data | Original packet preservation; retained development replay and mock transport tests | Paired live packet stream with device print missing |
| 2 Decoding | Observed0x10 signed flow, supported0x20 timed-volume packets; exact steps preserved | Firmware format/unit/negative encoding not independently certified |
| 3 Timing/units | Device-clock mode uses declared step; fixed mixed host/device comparison bug. Volume-step mode uses host receipt times | USB batching/latency and device clock correspondence need real trace |
| 4 Integration | Volume-step accumulates declared signed volume; time-step integrates flow*step; shared Expiration for chart/calculation | Integration/filter response/start threshold requires paired validation |
| 5 BTPS | No arbitrary coefficient copied from printed1.092 | Device internal/stream BTPS state, temperature/pressure/humidity not available |
| 6 Start/Time Zero | Expiratory segment threshold0.08 retained; corrected peak-tangent t0 and V(t0) with independent analytic tests | Origin is reconstructed, truncated near onset; not validated device Time Zero |
| 7 Measured indices | FVC end volume, FEV1 interpolation at segment1s, PEF max, fractional-volume flow/interval FEF | FEV1 time-origin/BTPS/filtering parity unresolved; no speculative re-timing |
| 8 Predicted | Existing sex-specific ERS/ECCS1993 equations; A eight and B five displayed predictions match | Coefficient family supported; firmware-specific population/age handling beyond evidence pending |
| 9 Selection | Independent FVC/FEV1/PEF maxima, FEF25–75 from highest FVC+FEV1; stable ties, actual IDs | Reference-supported and matches B; exact firmware handling/clinical acceptability still unverified |
| 10 Rounding | Full precision internally/JSON,2decimal volumes/flows,1ratio,0percent | B88.6 vs88.63 reproduced by quotient of printed3.74/4.22; unrounded device inputs unavailable |
| 11 Quality | Short UNVALIDATED status, descriptive repeatability only | Cough/leak, calibration, maneuver acceptance, gradeA equivalence not established |
| 12 Interpretation | Independent LLN comparison or clearly sourced clinician entry | MIR diagnosis/grading engine not reproduced or clinically validated |

## Printed-output regression fixture

app/Tests/Fixtures/mir-reference-b.json is explicitly an anonymized PRINTED OUTPUT reference, not raw measurement. Its three rows are used only to test aggregation and presentation. They do not generate fake raw traces.

| B parameter | Existing equation full precision | Printed pred. | Printed best | Printed % |
|---|---:|---:|---:|---:|
| FVC | 3.9624 |3.96|4.22|107|
| FEV1 |3.286|3.29|3.74|114|
| FEV1/FVC |79.29|79.3|88.6|112|
| PEF |8.3276|8.33|9.65|116|
| FEF25–75 |3.9896|3.99|5.16|129|

B input: male44,164cm,74kg, no group selected. A input: male41,182cm; eight rounded predictions5.08,4.15,79.8,9.56,8.28,5.28,2.34,4.47 also match existing equations. Weight is retained for identification/BMI, not inserted into ERS equations. User-entered age is used directly; no birthday/HTML age inference. Existing applicable range is18–70years, male155–195cm/female145–180cm; Other has no assumed ethnicity factor.

B's FEF25–75 best5.16 comes from PRE1 (largest FVC+FEV1), despite PRE3 having5.17. PRE3 ratio88.7 is not selected as an independent maximum. A additionally supports independent PEF (PRE3 5.72 vs PRE1 5.66). Current ratio is largest FEV1 / largest FVC; B alone cannot distinguish this from same-best-maneuver policy because both maxima lie in PRE1. Several paired tests where maxima differ are needed. No equation constants were tuned.

## BEV / EVol / VEXT

ATS/ERS peak-tangent construction:
- t0 = tPeak - V(tPeak)/PEF
- BEV = interpolated V(t0)

The old expression PEF*tPeak - V(tPeak) equals PEF*t0, a tangent-related volume, not the actual pre-Time-Zero exhaled volume. It is replaced. Independent test: Q(t)=10t, V(t)=5t² to peak0.2s; PEF2L/s, t0=.1s, BEV=.05L (old expression .2L). Another irregular piecewise-linear test independently expects80mL. Nonmonotonic/nonfinite/zero-slope/unbracketed inputs are rejected rather than clipped to0.

This proves the mathematical operation for supplied samples. It does not certify the samples: host timestamps in volume-step mode, quantization, inferred first interval, expiratory threshold, filtering and BTPS can affect early volume. JSON records source inadequacy explicitly. FEV1 retains the existing segment origin; it has not silently been moved to an unverified device Time Zero.

Printed EVol110/132/105mL and HTML VEXT132mL are a clue, not an established mapping/selection rule. Automatically choosing maximum EVol is not implemented. An operator can enter VEXT with name, source and explicit review; JPG and JSON distinguish manual entry. Unavailable remains null. Per-maneuver calculated BEV remains separately traceable.

## Primary references consulted

- [ATS/ERS2019, Figure1 and start-of-test definition](https://pmc.ncbi.nlm.nih.gov/articles/PMC6794117/) — tangent Time Zero, actual pre-Time-Zero volume; adequate sampling/acceptability.
- [ATS/ERS2005 spirometry standard](https://www.thoracic.org/statements/resources/pft/PFT2.pdf) — result selection and measurements.
- [MIR Spirodoc manual, Rev2.9](https://www.spirometry.com/media/spirometry-wp-content/uploads/2020/03/Spirodoc_Manual_EN_-REV.2.9_V.pdf) — best-test largest FVC+FEV1 in a related MIR family; supporting evidence, not proof of this Spirolab firmware.
- [MIR official Spirodoc parameter list](https://spirometry.com/media/documents/Spirodoc_v5_EN.pdf) — starred best FVC/FEV1/PEF notation.
- [NIH spirometry protocol](https://www.ncbi.nlm.nih.gov/projects/gap/cgi-bin/GetPdf.cgi?id=phd004296.2) — FEF25–75 from largest-sum acceptable maneuver.
- [ERS/ECCS1993 reference](https://doi.org/10.1183/09041950.005s1693) — existing equation family retained.

To close measured parity: acquire deidentified simultaneous device/independent-app maneuvers with known step/clock/BTPS conditions, device results and raw timing, including cases with maxima in different trials. Compare each layer before changing formulas. These hardware-dependent checks remain pending.
