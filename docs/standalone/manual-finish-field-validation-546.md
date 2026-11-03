# MIR manual finish — release 5.4.6 evidence

## Scope

The owner reports two successful, operator-run maneuvers using the existing experimental manual-finish patch: different correct values appeared in the table, click-to-registration was under one second, and no error was observed. Release 5.4.6 retains that patch's acquisition logic; finalization changes version identifiers, removes the experimental title/tooltip warning and updates documentation/package metadata. No new USB command or clinical calculation is introduced.

## Read-only verification of the two captures

The local device log and the corresponding saved examination were inspected without opening USB or starting a maneuver. Patient identifiers, serial numbers, raw blocks and clinical measurements are deliberately omitted from this repository.

| Check | Capture 1 | Capture 2 |
|---|---|---|
| Manual-finish ESC send attempt / successful send log | exactly one / one | exactly one / one |
| Order | ESC → packet `0x04` → LAST | ESC → packet `0x04` → LAST |
| ESC-to-LAST log interval | 316 ms | 314 ms |
| LAST length | 160 bytes | 160 bytes |
| Abort in capture log | none | none |
| Logged LAST equals saved DeviceValues.Raw | yes | yes |
| Capture sequence matches own maneuver | yes | yes |
| Completed, associated, valid device block | yes | yes |
| OperatorStopped, simulation or replay | none | none |

Both raw result blocks differ, and all four principal fields differ between captures. The saved examination contains two trials and no failed capture or export reference. These facts support that the previous result was not reused in these two maneuvers; they do not establish behavior for all possible hardware failures.

The existing big-endian LAST fields at offsets 16/18/20/26, divided by 100, match the respective saved FVC/FEV1/PEF/FET. A local, untracked read-only verifier loaded this same examination into the actual MainForm and report code. It checked the actual grid cells, report-detail rows, saved measurements and available numerical PEF/FEV1 chart markers against each capture's LAST, using display rounding. The report was rendered and disposed in memory. No patient report was saved, submitted or committed. There was no pre-existing report export or independent official MIR output available for comparison; independent numerical parity is **not claimed**.

## Software verification

`app\test.bat`: 11 existing suites, **964 PASS / 0 FAIL / 10 SKIPPED**. The optional external real-capture fixture was not supplied. Existing acceptance `MirBestValuesTests.exe mir-best-values-test-output --require-pending-end`: **127 PASS**. Existing cases cover pending/next-packet completion, simultaneous completion, repeated finish, absent end timeout, failed ESC, USB read failure, cancellation while waiting, natural completion, LAST association/duplicate rejection, official values and clinical exclusions. No additional synthetic test cases were added during finalization.

The isolated packaging self-test is separate from the distributed executable. The released x86 program has no test-mode dependency. The original experimental package and the inspected user examination, backup, settings and log are checked for byte preservation.

## Limits

Only the two owner-run manual finishes are confirmed on the connected hardware. Natural completion and immediate cancellation retain their existing code paths and software coverage; no new operator-run hardware test of those paths was performed in this finalization. No live HLine write was performed. Broader device/firmware compatibility, clean-PC acceptance, independent MIR output parity, BTPS and production HLine validation remain **PENDING**. Existing quality assessment and provenance gates still determine whether any maneuver can enter the clinical result.
