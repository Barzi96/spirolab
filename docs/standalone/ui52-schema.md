# Schema2 extension — SpiroLab5.2

> Historical 5.2 schema. Current 5.3.1 uses `SelectionWorkflowVersion=3`: no primary/site radio, `bestValues` is the authoritative all-eligible aggregate, `siteExport` is retired, and report choices affect display only. See [best-values-531.md](best-values-531.md).

No scientific equation or device decoder changed. SelectionWorkflowVersion=2 identifies the simplified workflow. Legacy0/1 finalized records retain their old selection/render behavior. When an old draft is opened for editing, saved site/primary choice is retained and report defaults preserve the prior visible subset; absent site choice is managed automatically. Current defaults never fill a historical center/operator.

## Two independent selections

| Stored field | Meaning |
|---|---|
| OperatorSelectedManeuverId | Backward-compatible stable ID now used for site export |
| SelectionMethod | automatic until a radio click; manual after a click; legacy automatic_suggestion remains a sticky explicit decision |
| SelectionTimestamp / SelectionOperatorName | Actual decision time and known operator; missing operator remains unavailable |
| ReportManeuverIds | Independent stable IDs, maximum3 |
| ReportSelectionManual | Once edited, no later maneuver silently fills or replaces choices |
| ReportSelectionTimestamp / ReportSelectionOperator | Automatic/default or manual report-selection provenance |

Defaults: one eligible maneuver is site-selected by unchanged largest FVC+FEV1 ranking. Automatic selection may improve after a new trial. Manual radio choice remains. Report defaults add the first eligible trials until3, then keep them. All attempts, including incomplete/invalid/rejected, remain in local history. Finalization requires min(3,eligibleCount) report choices; zero eligible cannot finalize.

## JSON groups

- siteExport.maneuverId / acquisitionSequence / selectionMethod / selectedAt / selectedBy.
- siteExport.measurements: FVC, FEV1, FEV1_FVC, PEF, FEF25_75 and VEXT. Values0–4 all come directly from this one maneuver's full-precision AnalysisResult; ratio uses that maneuver's existing computed ratio. No independent maxima are substituted.
- siteExport.measurements.VEXT: clinic convention for the HLine `vext` field — the highest EVol strictly below 200 mL among the real (usb_live) maneuvers of this examination, independent of the radio choice. It carries valueSource, sourceManeuverId, sourceAcquisitionSequence, selectionRule and selectedManeuverEvolMl, and is null when no maneuver qualifies. `vextSelection` repeats the rule, source, method and problem text. The legacy exam-level and per-maneuver manual review values are preserved as local records (`spirometry.VEXT`, `maneuvers[].vextReview`) but never stand in for the HLine value.
- reportSelection.maneuverIds / method / selectedAt / selectedBy / workflowVersion.
- bestReportedValues: aggregation of the explicitly report-selected eligible pool in workflow2. Existing rules retained: independent FVC/FEV1/PEF maxima, FEF25–75 from largest sum maneuver, ratio of maxima. Exact firmware ratio/acceptability rules remain unverified as already documented.
- spirometry: backward-compatible alias of bestReportedValues. spirometryRole explicitly declares this alias; a future site adapter must consume siteExport.measurements.
- report.displayedManeuvers: precisely selected report IDs in original acquisition order, with original PRE sequence, independent of site choice.
- primaryManeuverSelection: compatibility alias of site selection; automatic recommendation is based on all eligible history, not the smaller report subset.
- maneuvers: every original attempt, stable ID/sequence, completion, source, validity, quality, rejection and BEV provenance. eligibleForSelection differs from includedInBestReportedValues when a usable attempt is not checked for report.
- examination: saved selected ISO date, distinct acquisition time, saved center/operator. UI uses BCL PersianCalendar for Jalali.
- provenance: clinical/hardware/BTPS status unchanged; transport remains local only.

JPG's BEST VALUES uses the report pool. Header/footer identify the site choice as site data, without forcing it into the plot. One/two report curves are labeled partial. No missing curves are fabricated. The shared frozen snapshot drives JPG and JSON; changing live source objects cannot mutate an open preview. Generated export paths/IDs/timestamps are saved separately as ExportReferences in local XML, so recording a path does not mutate the frozen clinical JSON.

No website connection, credentials, requests or upload were added.
