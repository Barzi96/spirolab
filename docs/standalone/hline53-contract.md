# HLine contract evidence — 5.3

Evidence is read-only. No clinical request was sent, no account credentials were supplied, and no patient or medical record was chosen for live testing.

## Sources and evidence level

The authorized private repository `Barzi96/cdc-anal` was read through the GitHub connector: `hl/client.py`, `record.py`, `subject_context.py`, `vitals.py`, `spirometry.py`, `spirometry_inputs.py`, `spirometry_contracts.py`, `clinical_case.py`, `source_semantics.py`, `webapp.py`, and relevant subject/record/pilot tests. Reader code establishes read contracts, not write contracts.

Local `spiro0.htm` was used only as a UI reference. Its SHA256 before and after inspection is `B72524479BE7F3D277C0C324A3FAF60007C04475400487D3E2270A79A79109BB`. It was not edited, copied into tests, or treated as an authorized test record. No values from its patient were used.

The accompanying `app.63d81e53_s3Zl.js`, SHA256 `79E93301CF183266ED095488AAAD6C9922951A6061767883B7DD99CB9EFE89B5`, establishes frontend request construction. This saved bundle is not a capture of a successful current server transaction. Neither the HTML nor the bundle is included in source or release packages.

## Authentication and minimal reads

- `GET /api/auth/csrf`: JSON `csrf`, session cookie jar.
- `POST /api/auth/login`: JSON `username`, `password`, `mfa_token`, `mfa_token_method: 1`; `X-CSRF-Token`, Origin and Referer. Nested `data.user` or `data.data.user` is checked, then CSRF refreshed and `/me` identity matched to the login identity. No automatic login retries.
- `GET /api/me`: repository nested-user formats plus the saved frontend's raw `data.id` format. An arbitrary nonempty object is not accepted as authentication.
- `GET /api/people?search=...&page_size=25&page_no=1`: exact `person_id_no` match only. Multiple matches and incomplete pages fail closed.
- `GET /api/people/{personId}`: person ID and national ID must both match search.
- `GET /api/medical-records?person_id_no=...&person_id_no_exact=true&page_size=200&page_no=1`.
- `GET /api/medical-records/{recordId}`: recheck IDs, admission date and status.
- `GET /api/medical-records/{recordId}/vitals/history`: only same-record values may populate height and weight. Other records are ignored. Missing or conflicting values stay blank. Every fetched measurement requires operator confirmation.
- `GET /api/medical-records/{recordId}/spirometry/history`: existing result prevents automatic replacement. Unlinked rows fail closed.

No other medical modules are queried. Person IDs and medical-record IDs are never interchangeable. Latest-date suggestions use parsed Gregorian `admission_date`, never numeric ID. Ties, missing dates/statuses require explicit selection. Record status eligibility is not established by reader code; therefore even a unique chronological suggestion requires operator confirmation. A live current-record selection remains pending.

Birth date is only mapped from `birth_date` in the repository-proven Gregorian date shape. `gender` uses the proven string vocabulary. `first_name`, `last_name` and `full_name` are preserved separately; a combined name is never split. Age for ERS uses the selected local examination date and a birth-date-based fractional age when available. Locally edited demographics are not written to the site.

## Spirometry write: established in saved frontend, not live verified

Symbols `Rm`, `qm`, `$m`, `Im`, `Fm`, `Lnt`, `Knt`, `Xnt` and `Znt` show:

| Operation | Frontend construction |
|---|---|
| Read current item | `GET /api/medical-records/{recordId}/spirometry` |
| Create | `POST /api/medical-records/{recordId}/spirometry`, JSON form copy, expected 201 |
| Update | `PATCH` same path, only dirty fields, expected 200 |
| Duplicate result | `duplicate_record`; frontend redirects to existing result |
| Read history | `GET /api/medical-records/{recordId}/spirometry/history` |

Create form contains `successful`, failure metadata, `result`, `result_severity`, `result_other`, `test_date`, `performed_by`, measured amounts/percentages for FEV1/FVC/PEF/FEF25–75, `vext`, `file_id`, bronchodilator fields, `srp`, `notes`.

**`fev1_fvc_percent` appears in read/display code but is absent from the create form.** The C# draft includes it as a measured value to verify, not as a proven writable JSON field. It must not be blindly sent back. The server may derive it.

`Knt` calls `$gdate` for test dates before dispatch. This proves conversion is necessary; the exact current wire format still requires network evidence. C# retains a Gregorian `DateTime` and does not invent a wire date string. `successful`/clinical interpretation are not automatically invented by the draft.

`source_semantics.py` verifies FVC/FEV1 litres and ratio percentage. Exact server flow units, VEXT units, precision/rounding, required clinical fields and current account permissions require confirmation. The draft keeps PEF/FEF in local L/s and reviewed VEXT in local mL with their source labels; it is not a complete live writer payload.

## JPG upload and association: saved frontend evidence

`715d`, `qX`, the `MedicalRecordFile` component, and the spirometry file picker establish:

- Upload: `POST /api/medical-records/{recordId}/files`.
- Multipart parts: `module_id=spirometry`, `medical_record_file` containing the original file, and `desc` (optional description in the component's submission path).
- Allowed JPG MIME: `image/jpeg`. The component uses the actual file MIME.
- Accepted file types include jpg/png/bmp/pdf; component size condition is **strictly less than 5,242,880 bytes**. Server validation remains unverified.
- Expected upload status is 201; file picker emits `response.data.data.id`, which is assigned to spirometry `file_id` by the parent form.
- File listing uses `GET /api/medical-records/{recordId}/files/{moduleId}` (here `spirometry`); the final segment in this GET is the module identifier, not evidence for a single-file metadata endpoint.
- Saved attachment URL: `/api/medical-records/{recordId}/files/{fileId}/dl?inline=1`.

Normal frontend order is upload first, then form create/update with `file_id`. There is no evidence of an atomic transaction or idempotency header. The application does not implement undocumented rollback/deletion or automatic retry. A completed file upload followed by a failed item create can leave a partial attachment; report it and retain local artifacts.

## Still required before any real clinical write

1. Authenticated read-only browser/network inspection of the current form and supported permissions, without modifying unrelated records.
2. A user-designated dedicated test person's national ID, internal person ID, medical-record ID, admission date/status, and verification that this is the intended test destination.
3. Explicit authorization for the particular create + JPEG association test operation and exact reviewed numeric/test inputs. The supplied HTML supplies none of this authorization.
4. Confirm required fields, date encoding, flow/VEXT units, precision, duplicate behavior, file response schema and allowed size/MIME against current requests. No speculative mutation requests.
5. Implement the live gateway only after that evidence. Reread the exact record, compare all six core values plus available predictions, verify `file_id` in the same record/module, and retrieve/compare the submitted JPEG bytes. HTTP 200/201 alone is insufficient.

No live gateway is shipped in 5.3. The production HTTP transport rejects all non-GET operations except `/auth/login`; injection of a mock gateway is used only by tests. Stage C is partial static discovery; Stage D has not run.
