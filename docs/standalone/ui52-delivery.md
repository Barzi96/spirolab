# SpiroLab5.2 — delivery and visual acceptance

## A. UI changes

Persistent patient sidebar and duplicated maneuver-selection/rejection/warnings/reason buttons removed from the clinical screen. Eight-column fill-width table replaces the wide history plus separate metrics grid. Patient registration is modal. Center/operator defaults and device diagnostics live in Settings. Exam-specific interpretation, reviewed VEXT, quality metadata and patient inspection live in Examination Details, opened from the patient-name link. No new dependency, driver change or numerical rewrite.

## B. Main screen

Compact RTL toolbar, masked-by-omission patient summary with name/age, top editable Jalali date, large two-axis live chart (stacked or side by side to preserve readable axis height), compact history, three action buttons. Connection indicator opens device settings. Solid active trace and dashed report-comparison traces share correct scales and existing Expiration samples. Screenshots01-main-empty,03-main-patient,04-main-five are actual launched WinForms windows, not drawn mockups.

## C. New patient dialog

Screenshot02-patient-dialog. Eight core fields; age accepted instead of birth date. Persian digits normalized. Required names/10-digit ID, age and anthropometric limits validated inline. Save creates a separate examination/session ID and local record. Cancel leaves the current session untouched. Existing optional patient metadata is preserved on clones. Earlier exam files are never replaced by a new session.

## D. Maneuver selection

Native radio glyphs with radio accessibility role and genuine checkbox cells; mouse, Space and accessibility default actions use persisted model decisions. Default radio is automatic; manual override sticky. Default report fills up to3 eligible maneuvers and becomes sticky after any checkbox edit. Fourth gives the required short Persian inline message. Tested site4 versus report1,3,5, restart, additional maneuvers and nonselected-history preservation. Legacy explicit-selection model tests remain covered with workflowVersion1.

## E. Date

Windows local date converted for UI using PersianCalendar and Persian digits. Month picker uses calendar APIs including leap-day handling. ISO date persists; typed Jalali edit and historical reopen tested. JPG and JSON share saved date. No HLine date synchronization.

## F. Settings

Screenshot05-settings. Exact center default retained, operator initially empty. MIRUSB detection/state/version, connection test and existing privacy-safe diagnostics export available. Developer simulation is a secondary link/menu and requires explicit activation. New examination clears simulation mode. No firmware/security/binding change.

## G. JPG

Existing2400x3394/300DPI/quality97 renderer preserved. Explicit report IDs only; original PRE numbers retained. Partial1/2 curve reports labeled. Independent site ID is identified without adding its curve if not checked. Screenshot06-report-preview and paired JPG/JSON demonstrate report1,3,5 and site4. Best-value rules unchanged within the selected report pool.

## H. JSON

Schema2 extended compatibly: siteExport is single-maneuver data with stable source ID; bestReportedValues and legacy spirometry are separate report aggregates. reportSelection records exact checkbox IDs and provenance. VEXT cannot carry from another maneuver; legacy unassigned exam-level value remains separately preserved. See ui52-schema.md. Future HLine work must read siteExport, not the legacy aggregate alias.

## I. Tests and visual evidence

Baseline263 checks passed before edits. Final428 checks: retained75+63+66+59 and165 focused clinical UI checks. Assertions referring to controls deliberately removed by this user request were migrated to the new controls/semantics; no measurement, capture, persistence or security coverage was deleted.

Actual forms launched and rendered for six required screenshots and twelve resolution/font-layout cases:1280x800,1366x768,1600x900,1920x1080 with100/125/150% equivalent control/font scaling. Tests check column widths, button caption fit, parent clipping, headers, plot size and at least one readable data row. Visual review found and fixed double RTL flow, auto-width root clipping, connection-label clipping and too-small history/summary rows. Vertical history scrolling is expected at small high-scaling layouts.

The physical Windows DPI setting was NOT changed;125/150% evidence is equivalent layout/font stress rather than native OS DPI acceptance. Actual compiled SpiroLab.exe was separately launched through Computer Use and exposed the expected5.2 window. Windows capture helper failed with “FrameArrived timed out” then “window capture timed out” on its one retry. No stale-coordinate input followed. Empty smoke-test process was closed. The deliverable PNGs were captured directly from actual native WinForms windows in the test host.

## J. Files

Portable/source ZIP, six screenshots, all twelve layout screenshots, example JPG/JSON and full test log are provided in the delivery directory. An HTML index links them for review. Exact paths and hashes are recorded in the generated delivery manifest.

## K. Git

Feature branch feature/clinical-ui-5.2, local commits, no push. Backup before changes: phase-backups/before-ui-5.2.zip. Prior distributions and patient data preserved. Final commit IDs and clean-tree state in delivery evidence.

## L. Genuine unresolved items

Real USB/second-PC/clinical firmware parity remain pending. Native OS125/150% and multi-monitor DPI behavior still need physical-system validation. External Windows capture helper timed out as above; this is not passed off as product screenshot evidence. No website integration was implemented.
