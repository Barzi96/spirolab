# Occupational-health field mapping — schemaVersion 2

The supplied `spiro.htm` was inspected locally as a visible results page. It is not an API or permission to send requests. No login, HTTP client, credential handling, browser automation or website transport is implemented.

| HTML/clinical field | JSON location | Unit and source |
|---|---|---|
| FVC | spirometry.FVC | L; independent largest complete usable maneuver FVC; source maneuver UUID |
| FEV1 | spirometry.FEV1 | L; independent largest FEV1; source UUID |
| FEV1/FVC | spirometry.FEV1_FVC | percent, not fraction; ratio of independent maxima |
| FEF25–75 | spirometry.FEF25_75 | L/s from maneuver with greatest FVC+FEV1 |
| PEF | spirometry.PEF | L/s; independent largest PEF; source UUID |
| VEXT | spirometry.VEXT | value null, mL, unavailable/not_validated |
| Test date/time | examination.testedAt | first acquisition timestamp, ISO 8601; not export time |
| Operator | examination.operatorName/status | manual input, unavailable when blank; no guessed login identity |
| Interpretation | interpretation | clinician-entered text when supplied; otherwise explicitly unvalidated algorithm |
| Patient | patient.nationalId | string; preserves leading zeros |
| Examination | examination.id / snapshotId | persisted examination UUID and fixed export snapshot UUID |
| Device | device | known source serial, model family not verified, hardware validation pending |

Each numerical measurement retains `value`, `unit`, `predicted`, `percentPredicted`, `valueSource`, `verificationStatus`, `missingDataStatus`. JSON preserves precision; report alone rounds. Missing values use null. Reference ERS/ECCS 1993 is declared; unsupported age/height/origin has null reference values.

BEV estimation is kept separately under maneuvers[].estimatedBEV, explicitly not equivalent to validated VEXT. The term extrapolated volume in standards is related to back extrapolation, but the HTML label alone does not establish algorithmic equivalence, time zero, BTPS correction, or validity of this implementation. No guessed VEXT appears in JPG or JSON. Source: ATS/ERS 2019 https://pmc.ncbi.nlm.nih.gov/articles/PMC6794117/ and ATS/ERS 2005 https://www.thoracic.org/statements/resources/pft/PFT2.pdf .

The JSON includes all original maneuvers in acquisition order; stable IDs and indices remain intact. The report displays at most three selected complete usable maneuvers in their original order with their original PRE indices (e.g. #2, #4, #5); it does not rename a sorted #5 to #1. Selection and independent best values are distinct.

`ExaminationSnapshot` deep-copies patient, maneuver signal, analysis and reference values once. Both JPG rendering and JSON consume that same snapshot. Original examination edits cannot change an open preview's export. Exports publish a unique directory atomically after JPEG/JSON verification. Duplicate snapshot publication is refused. Existing reports are never overwritten.

Schema v1 exports are not silently imported as v2; old `.spiro` XML records (Version=1) load with Version=2 and LegacyRecord=true. Existing history remains; normal save uses a .bak. Missing old maneuver IDs are assigned on migration and persisted at next save. Old operator/interpretation remain unavailable until entered. No invented clinical approval is inferred from legacy OperatorAccepted flags.

Source categories: simulation, replay, usb_live. Simulation/replay: NON-CLINICAL TEST DATA. Current live reconstruction: DEVELOPMENT — UNVERIFIED MEASUREMENTS. Hardware and clinical validation are pending. Quality grade is unavailable unless entered manually; manual entry is not independently verified. No occupational exposure, smoking history, serial longitudinal comparisons or website-only metadata is added.
