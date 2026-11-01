# Validation record — SpiroLab5.1, 2026-11-01

Baseline before edits: 75 regression +63 standalone checks PASS (138). Final corrected application: 75 retained +63 retained +66 new checks PASS (204). Six separate private-driver inspection checks also PASS. The old obsolete CurvesPath test causes one expected compiler warning; no test was removed to hide failure.

New checks cover independently derived analytic Time Zero/BEV, irregular interpolation and invalid inputs, device-clock vs host polling, 12-byte IOCTL structure, distinct communication stages, both printed references' scalar predictions, B selection/rounding using printed-output fixtures ONLY, settings persistence/recovery, calendar conversion/leap dates, manual VEXT provenance, separate exam/capture dates, finalized JSON equality after XML reload and GUI locks.

Retained checks include mock natural-end/cancel/read-failure, no silent simulation, packet preservation, 1–5 maneuvers, single-page GUI, JPG codec/size/DPI, snapshot export, rollback, unique publication and record migration. Finalized analyses persist/reload without recalculation; legacy drafts still reconstruct saved signals, and missing historical selected dates derive from original capture date.

Driver checks inspect the signed exported package without installing, then reject tampering, escaping paths, wrong architecture, missing components and escaping INF. Actual second-PC installer branch was not executed here.

Full results: app/v51-test-results.txt and app/driver-test-results.txt; copies included in distribution Docs. Packaged EXE self-test additionally checks persistence/JPG/JSON on this development host.

Pending: clean Windows A, physical live USB B, second-PC installation/binding, real driver timeout/cancel/reconnect, 32-bit Windows driver package, BTPS/start/timing/signal/firmware parity, automatic VEXT selection, quality grading and clinical interpretation. No hardware or clean-PC pass is inferred from software checks.

## 5.1.1 addendum validation
All204 retained checks PASS plus59 new maneuver-selection checks:263 total. One/five/125 saved attempts; GUI129; explicit nonrecommended primary, real selection button persistence/reopen/append, rejection/invalid/incomplete retention, independent aggregates, legacy migration, matching JPG/JSON snapshot and40MiB rollback guard. Full current log is full-test-results.txt. Hardware/second-PC status is unchanged; tests use simulation and retained USB mocks.
