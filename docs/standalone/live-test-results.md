SpiroLab 5.3.1 — HLine live restore test
Date: 2026-11-02 (Asia/Tehran)
Authorization: explicit owner approval to write to the designated historical test record.
Data source: owner-provided local backup (saved HLine record page + original JPG).
No patient name, national ID, record ID, cookie, token or clinical values are recorded here.

Preconditions
- Firefox session adopted read-only; GET /api/me valid.
- Record identity verified against the person and record ID.
- Record belongs to the authenticated center; scopes mrsp_u/p/i/nf present.
- Spirometry section empty; files before = 0.

Restored data
- Values and date came from the owner-provided backup, not from invented test numbers.
- All amount and percentage fields, VEXT, test date and interpretation were checked
  against the backup in the local session.
- The original interpretation was "Not recorded"; result and result_other were sent as null.

Write result
- multipart upload: 1
- spirometry POST: 1
- response: 201
- coordinator state: Confirmed
- already_present: False
- files after: 1
- readback amounts/percentages/VEXT/date matched the draft
- readback file_id present: True
- downloaded JPG was byte-for-byte identical to the backup JPG

Duplicate protection
- second run with a fresh journal against the existing result
- state: Confirmed
- already_present: True
- uploads: 0
- spirometry POSTs: 0
- files stayed 1

Independent read-only verification
- GET test date matched the restored date
- result null: True
- result_other null: True
- performed_by present: True
- all amount and percentage fields and VEXT matched
- files: 1
- file download HTTP 200; byte identity match: True
- RESULT: PASS

Limits
- This proves the write contract for this one designated historical record only.
- Production writes remain disabled by default and require explicit owner approval.
- PATCH/correction, real duplicate_record response, real 419, network timeout recovery,
  server file-size limits, module status transition and other interpretation categories
  were not exercised live.
