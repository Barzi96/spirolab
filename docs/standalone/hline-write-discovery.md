# HLine spirometry write and JPG upload — discovery report

Date: 2026-11-01
Status: **discovery only**. No production writer was added and **no write request was sent**
to HLine. The existing read-only guard in `HLineHttpTransport` is unchanged.

## 1. Evidence and method

| Source | What it proves |
|---|---|
| Production SPA shell `https://hline.ssu.ac.ir/` and bundles `js/app.63d81e53.js` (3,238,726 B), `js/vendor.7ea03751.js` (2,325,568 B) | Exact URLs, HTTP verbs, payload fields and status handling used by the real HLine UI. Downloaded read-only with the adopted Firefox session. |
| `GET /api/me` with the real session | Current account can authenticate; panel type and permission codes (below). |
| `GET /api/medical-records/0/spirometry` and `GET /api/medical-records/0/files/spirometry` | Routes exist (controller returned the application 404 envelope), not the generic router 404. No record `0`, so no patient data was read. |
| `app/Src/HLine/HLineClient.cs` | Existing lookup, record identity checks, session acquisition. |
| `Barzi96/cdc-anal`, `Barzi96/cdc-fata` | Authentication, Firefox session and lookup only. **Neither repository contains a spirometry writer or a file upload call**; they cannot serve as a proven write reference. |

Labels used below:

- **LIVE** — observed in a real request during this investigation.
- **SOURCE** — read directly from the production HLine frontend bundle; the operation was
  not executed against the server.
- **ASSUMED** — not established yet; requires the controlled test record.

## 2. Endpoint map

All paths are relative to `https://hline.ssu.ac.ir/api`. The axios client uses
`baseURL: "/api"` and sends every request with `X-CSRF-Token` (except `/auth/csrf` itself).

| Purpose | Method | Path | Success | Evidence |
|---|---|---|---|---|
| CSRF token | GET | `/auth/csrf` | 200 `{csrf}` | SOURCE + LIVE (existing client already uses it) |
| Read spirometry of a record | GET | `/medical-records/{recordId}/spirometry` | 200, `data` = object or null | SOURCE; LIVE route check with id `0` returned app 404 |
| Create spirometry | POST | `/medical-records/{recordId}/spirometry` | 201, `data` = saved object | SOURCE |
| Update spirometry | PATCH | `/medical-records/{recordId}/spirometry` | 200, `data` = saved object | SOURCE |
| Review / finish review | PATCH | `/medical-records/{recordId}/spirometry/review` | 204 | SOURCE |
| Delete the spirometry module data | DELETE | `/medical-records/{recordId}/spirometry` | 204 | SOURCE |
| Spirometry history of the person | GET | `/medical-records/{recordId}/spirometry/history` | 200, `data` = array | SOURCE + already used by `HLineClient.HasSpirometry` |
| List files of one module | GET | `/medical-records/{recordId}/files/{moduleId}` | 200, `data` = array | SOURCE (`medicalRecordFile/fetch` store action) |
| Upload a file | POST | `/medical-records/{recordId}/files` | 201, `data.data.id` = new file id | SOURCE |
| Download / verify a file | GET | `/medical-records/{recordId}/files/{fileId}/dl?inline=1` | 200 with file bytes | SOURCE (`$fileUrl`) |
| Delete a file | DELETE | `/medical-records/{recordId}/files/{fileId}` | 204; 409 if still referenced | SOURCE |

Note: the second segment of the **list** route is the module id (`spirometry`), not a file id.
The frontend store action is named `fetch` and calls
`GET /medical-records/{id}/files/{module_id}`.

### 2.1 Create vs update

- The form is in *create* mode when the record's `spirometry` module status is `NoData`;
  otherwise it is in *edit* mode.
- Create: `POST`, expects **201**. On success the UI stores `data.data`, refreshes the
  medical record and redirects to the view page.
- Update: `PATCH`, expects **200**. In edit mode the UI deletes untouched fields from the
  payload and sends only dirty fields. Sending the full field set is expected to work
  because the create payload is a superset, but this is **ASSUMED** until the test record
  confirms it.
- Duplicate create: the client examines `response.data.errors` for `duplicate_record`
  and shows «اسپیرومتری این پرونده قبلا ثبت شده». **ASSUMED**: the HTTP status for that
  validation error is 422 (Laravel validation convention); only the error token and the
  client handling are SOURCE-proven.
- The delete endpoint is **not** part of normal submission and must not be used to
  “replace” a result; use PATCH.

## 3. Authentication, CSRF and permissions

- Session: the existing Firefox-session adoption is sufficient. `GET /api/me` returned
  **200** during this investigation with panel type `medical_center`.
- CSRF: `GET /api/auth/csrf` returns `{ "csrf": "..." }`. The header is
  `X-CSRF-Token: <token>`.
  - The SPA fetches the token once per page load and attaches it to every subsequent
    request, including GETs.
  - The existing C# client deliberately sends CSRF only on non-GET and currently discards
    the token after Firefox adoption. **A future writer must fetch a fresh token after
    adoption and before the first write**; otherwise the request will fail with 419.
  - Whether a token is single-use is still **ASSUMED**. Safe rule: fetch once per
    submission batch, reuse; on 419 refetch once and retry.
- Permission codes used by the UI (from the production bundle):
  - `mrsp_u` — create/update spirometry
  - `mrsp_p` — set `performed_by`
  - `mrsp_i` — set interpretation (`result`, severity, text)
  - `mrsp_nf` — create/upload file
  - `mrsp_rf` — read/download file
  - `mrsp_dm` — delete module data
  - `mrsp_rv` — review
- **LIVE**: the current account has `mrsp_u`, `mrsp_p`, `mrsp_i`, `mrsp_rf`, `mrsp_nf`,
  `mrsp_dm`; it does **not** have `mrsp_rv`. The account can therefore create/update and
  upload, but cannot perform the review action.
- The UI also requires the record to belong to the user's center
  (`record.medical_center_id == user.medical_center_id`) before showing update/file
  actions.

## 4. Spirometry JSON payload (SOURCE)

Create sends the whole form; update can send only changed fields. Field list and types:

```json
{
  "successful": true,
  "failure_notes": null,
  "failure_reason": null,
  "result": "Normal",
  "result_severity": null,
  "result_other": null,
  "test_date": "2026/11/01",
  "performed_by": "<current HLine user id>",
  "fev1_amount": 2.31,
  "fev1_percent": 78.4,
  "fvc_amount": 3.42,
  "fvc_percent": 82.1,
  "pef_amount": 6.10,
  "pef_percent": 74.2,
  "fef2575_amount": 2.05,
  "fef2575_percent": 61.3,
  "vext": 145.0,
  "file_id": "<id returned by the upload>",
  "bronchodilator_type": null,
  "bronchodilator_dose": null,
  "bronchodilator_test_date": null,
  "bronchodilator_performed_by": null,
  "bronchodilator_fev1_amount": null,
  "bronchodilator_fev1_percent": null,
  "bronchodilator_fvc_amount": null,
  "bronchodilator_fvc_percent": null,
  "bronchodilator_pef_amount": null,
  "bronchodilator_pef_percent": null,
  "bronchodilator_fef2575_amount": null,
  "bronchodilator_fef2575_percent": null,
  "bronchodilator_vext": null,
  "bronchodilator_file_id": null,
  "srp": null,
  "notes": null
}
```

Rules established from the form definition:

- `test_date`: **Gregorian** `YYYY/MM/DD`. The UI validates a Jalali date, then converts
  it with `$gdate` before sending. **Do not send Jalali and do not reuse
  `ExaminationDates.Format` (`yyyy-MM-dd`)** unless the test record proves the server also
  accepts the dash form.
- `performed_by`: the HLine **user id string** (e.g. an alphanumeric id, not a display
  name), defaulted to the current user when creating.
- `fev1_amount`, `fev1_percent`, `fvc_amount`, `fvc_percent`, `pef_amount`,
  `pef_percent`, `fef2575_amount`, `fef2575_percent`, `vext`: JSON numbers; `null` when
  unknown. The UI validates positive numbers and FEV1 < FVC. `fev1_fvc_percent` is **not**
  a write field (the UI derives it for display).
- `vext`: HLine labels it “E-Volume” and the form example is `23.25`. Its unit is
  **ASSUMED** to be mL, matching the clinic VEXT; this must be confirmed on the test
  record. Do not convert without evidence.
- `result` enum (exact strings): `Normal`, `Probably Normal`, `Obstructive Pattern`,
  `Restrictive Pattern`, `Mixed`, `Small Airway Obstruction`,
  `Not Acceptable/Reproducible`, `Other`.
- `result_severity` enum: `Mild`, `Moderate`, `Moderately Severe`, `Severe`,
  `Very Severe` (nullable).
- `result_other`: required when `result == "Other"`.
- `successful`: `true` for a performed test; when `false` the UI clears result, file and
  all numeric fields and expects `failure_reason` / `failure_notes`.
- `srp`: boolean “significant decline versus previous”; nullable. Do not invent it.
- Bronchodilator block is for post-BD tests; send all `null` for a PRE-only report.
- `file_id` is the string id returned by the upload; `null` if no attachment.

## 5. Upload contract (SOURCE)

```
POST /api/medical-records/{recordId}/files
Content-Type: multipart/form-data; boundary=...
X-CSRF-Token: ...

module_id = spirometry
medical_record_file = <binary>; filename="report.jpg"; Content-Type: image/jpeg
desc = <optional text>          # omitted by the picker when empty
```

Response `201` with `data.data` containing at least `id`; the picker assigns
`form.file_id = data.data.id`.

- The spirometry picker explicitly configures
  `allowed-file-ext: ["jpg","png","pdf"]`, `list-file-ext: ["jpg","png"]`,
  `convert-pdf-to-image: true`.
  Therefore **JPG is an intentional, first-class attachment type** for this module; PDF is
  converted to an image before upload. The “(PDF)” label is misleading.
- Generic `MedicalRecordFile` limit: **5 MiB** (`5 * 1024 * 1024`). The sample Spirolab JPG
  is 2400×3394 at 300 DPI and 788,752 bytes, comfortably inside the limit.
- `file_type` is the extension (`jpg`, `png`, `pdf`) derived from the uploaded filename.
- File list rows expose `id`, `module_id`, `file_type`, `file_size`, `file_desc`, and the
  download URL.
- **ASSUMED / not yet verified**: the server-side MIME allowlist. The client accepts JPG,
  and the production flow depends on JPG uploads for this module, but only a controlled
  upload can prove the server accepts `image/jpeg`. This is the main upload blocker.

## 6. Association between file and spirometry result

1. Upload creates a row under the record with `module_id = "spirometry"`.
2. The returned `id` is stored in the spirometry JSON as `file_id`.
3. The print page requires `spirometry.file_id` (the bundle's section map lists
   `spirometry_file: requiredKeys ["spirometry", "spirometry.file_id"]`).
4. Verification: `GET /medical-records/{recordId}/files/spirometry` must contain that id,
   and `GET /medical-records/{recordId}/spirometry` must return the same `file_id`.
5. `GET /medical-records/{recordId}/files/{fileId}/dl?inline=1` must return 200 and
   `image/jpeg` with the expected byte length.

**ASSUMED**: whether the server rejects a `file_id` that belongs to another module or
record. The UI filters by module, but server-side validation was not exercised.

## 7. Recommended operation order

1. Confirm session (`/me`) and fetch CSRF.
2. Confirm the destination record (section 8).
3. `GET /medical-records/{recordId}/spirometry`:
   - existing object with identical values and `file_id` → already saved, stop;
   - existing object with different values → update path (`PATCH`);
   - null/empty → create path (`POST`).
4. Upload the JPG (unless a journaled file id for the same report hash already exists).
5. `POST` (create) or `PATCH` (update) the spirometry JSON with `file_id`.
6. Verify with `GET /spirometry`, `GET /files/spirometry`, and a download probe.
7. On failure after a successful upload, delete the just-uploaded file (204) unless the
   spirometry save succeeded or the file is referenced (409).

Upload before save is required by the data model (`file_id` is part of the save payload).
The reverse order (save with null, then PATCH the file id) is not used by HLine and should
not be invented.

## 8. Locating and verifying the medical record (existing code)

The current client already implements the safe lookup:

- `GET /people?search={nationalId}&page_size=25&page_no=1`, then exact national-id match;
- `GET /people/{personId}` and identity check;
- `GET /medical-records?person_id_no={nid}&person_id_no_exact=true&page_size=200&page_no=1`;
- `GET /medical-records/{recordId}` and check `id`, `person_id`, `person_id_no`,
  `admission_date`, `status` (no silent retarget);
- latest record must have a unique date (`LatestUnambiguous`).

For submission, additionally check:

- `record.medical_center_id` equals the current user's `medical_center_id`;
- `GET /medical-records/{recordId}` contains a `modules` entry with
  `module_id == "spirometry"`; its `status` tells create vs edit;
- after save, the same `GET` shows the module no longer `NoData` (expected; **ASSUMED**
  that saving sets it automatically).

## 9. Duplicate writes, retries and uncertain outcomes

Risks:

- `POST /spirometry` is a create; a blind retry can produce a second request and a
  `duplicate_record` error (or, worse, a duplicate if the server does not enforce
  uniqueness — the UI clearly expects an error token, so a uniqueness rule likely exists).
- `POST /files` is not idempotent: each retry creates another file. Orphan files are
  possible if the JSON save fails.
- A timeout does not mean failure. The server may have committed.

Strategy:

1. **Read before write**: `GET /spirometry` decides create vs update.
2. **Local submission journal** under `%APPDATA%\SpiroLab`: record id, report SHA-256,
   values hash, uploaded file id, last known state. This survives restarts and prevents
   re-uploading the same JPG.
3. **Treat `duplicate_record` as “already exists”**, then `GET` and compare; do not retry
   `POST`.
4. **On network timeout**: `GET /spirometry` and the file list. If the expected row is
   present with matching values/file → success. If absent → retry once. If present with
   different values → stop and report a conflict; never overwrite silently.
5. **PATCH is naturally repeatable** for the same values; still verify afterwards.
6. **File retry**: look up `GET /files/spirometry` for the journaled id or a row with the
   same name/size/creation time before uploading again.
7. Distinguish outcomes: `Saved`, `AlreadySaved`, `Rejected` (validation), `Forbidden`,
   `SessionExpired`, `UnknownNeedsVerification`.

## 10. Architecture comparison

| Option | Verdict |
|---|---|
| **A. Direct C# HTTP/API calls using the adopted Firefox session** | **Recommended.** The contract is fully visible in the production bundle; the app already has session adoption, CSRF plumbing (needs a fresh token), lookup and record identity checks. Multipart upload is straightforward with `HttpWebRequest`. |
| **B. Separate Web2API service** | **Not justified.** No technical need was demonstrated: no browser-only signature, no WebSocket, no CAPTCHA in the write path. Adding a server would add deployment and credential-handling risk. |
| **C. Browser automation** | Fallback only. Useful if the server later enforces a browser-only header or anti-automation check; nothing observed so far requires it. |

## 11. Actual tests performed

| Test | Result |
|---|---|
| Fetch SPA shell with adopted Firefox session | 200, 744 B |
| Fetch `js/app.63d81e53.js` / `js/vendor.7ea03751.js` | 200 / 3,238,726 B and 2,325,568 B |
| Trace spirometry endpoints in the bundle | `GET/POST/PATCH/DELETE /medical-records/{id}/spirometry`, `PATCH .../review`, `GET .../history` |
| Trace file endpoints | `POST /medical-records/{id}/files`, `GET /files/{moduleId}`, `GET /files/{fileId}/dl`, `DELETE /files/{fileId}` |
| Trace upload form fields | `module_id`, `medical_record_file`, optional `desc`; 201; `data.data.id` |
| Trace JPG allowance | picker `allowed-file-ext ["jpg","png","pdf"]`, `list-file-ext ["jpg","png"]`, PDF→image conversion |
| `GET /api/me` | 200; panel `medical_center`; spirometry scopes present (`mrsp_u/p/i/nf/rf/dm`, no `mrsp_rv`) |
| `GET /api/medical-records/0/spirometry` | 404 application envelope `{"data":{},"status":1,...}` (route exists; record missing) |
| `GET /api/medical-records/0/files/spirometry` | 404 application envelope (route exists; record missing) |
| `OPTIONS` on the same paths | 404 generic (Laravel does not register OPTIONS) |
| API docs probes (`/api/docs`, `/openapi.json`, …) | 404; no schema available |
| Reference repositories | no write/upload implementation found |
| **Controlled upload / POST / PATCH** | **Not performed — no test record was supplied** |

## 12. Unknowns / blockers

1. **Test record**: a dedicated medical-record id (or the patient national id that owns it)
   is required for controlled writes. Nothing was written to any patient.
2. **Server-side JPG acceptance**: the client allows JPG; the server MIME/extension
   allowlist is unverified.
3. **Validation rules**: which fields are required, whether `file_id` may be null, whether
   `performed_by` must be a user of the same center, and the real status code/body for
   `duplicate_record`.
4. **VEXT unit**: mL versus something else; confirm against a test record.
5. **CSRF lifetime**: single-use vs reusable; 419 recovery path must be tested.
6. **File/module binding**: whether the server rejects a `file_id` from another module or
   record.
7. **Module status transition**: whether POST/PATCH sets `modules[spirometry].status`
   automatically, and whether review is required before the record is considered complete.
8. **Duplicate constraint**: whether it is a DB unique index or only a controller check.
9. **Rate limits / audit**: not observed; assume none until tested.
10. **Client-version header**: the SPA sends `X-Web-Client-Version` on every request; the
    existing C# client does not. It is **ASSUMED** optional until a live write proves it.

## 13. Recommended implementation plan for one «ارسال به سایت» button

**Phase 0 — controlled contract test (after the test record arrives).**
Add a temporary, internal, opt-in probe (not shipped) that performs exactly:
`GET /spirometry` → one JPG upload → one POST or PATCH → verification GETs.
Log only status codes, keys, and sanitized values; no cookies, tokens or patient data.
Record the actual server response for duplicate and validation cases.

**Phase 1 — submission service (no UI change yet).**
`HLineSubmissionService`:
- ensure session + fresh CSRF;
- confirm record identity/center/module;
- read current spirometry;
- upload multipart with `module_id=spirometry`, max 5 MiB, JPG;
- POST or PATCH the payload of section 4;
- verify via GET and download;
- journal state; delete orphan file on failure.
- Never log payload values or file bytes.

**Phase 2 — one-click UI.**
Replace the current toolbar action with a single «ارسال به سایت» button:
no confirmation checkbox, no extra dialog; a background task with status text
(«در حال ارسال…», «ثبت شد», «قبلاً ثبت شده», «خطا: …»). Session expiry reuses the
existing Firefox reacquisition path.

**Phase 3 — tests.**
- mock HTTP server: exact multipart shape, JSON fields, date format, create/update,
  duplicate, timeout-then-verify, 419 retry, orphan cleanup;
- live test only on the dedicated record;
- regression: no USB, calculation, maneuver-selection, VEXT, JPG-rendering, lookup or
  Firefox-login changes.

Production release only after the live test proves: JPG accepted, POST/PATCH accepted,
`file_id` linked, values/distribution readable back, and no duplicate on retry.

## 14. Bottom line

The write surface is now known and is small: one JSON save endpoint, one multipart upload,
one file list/download and one delete. The production UI already accepts JPG for the
spirometry attachment (PDF is converted), so a direct C# implementation is technically
the simplest path and does not need a Web2API service.

What is still missing is not design but **proof**: no upload or save has been executed
against HLine yet. With a dedicated test record, the remaining unknowns can be closed with
two or three controlled requests.
