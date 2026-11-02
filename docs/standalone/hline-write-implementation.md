# SpiroLab 5.3.1 — پیاده‌سازی ارسال مستقیم به HLine

## خلاصه

مسیر «ارسال به سایت» اکنون یک پیاده‌سازی مستقیم C#/HTTP دارد. این مسیر از
مرورگر، Web2API یا import XML استفاده نمی‌کند. ارسال بالینی به‌طور پیش‌فرض
**خاموش** است و تا تعیین یک پروندهٔ تست واقعی و تأیید مالک، هیچ دادهٔ بیمار
نوشته نمی‌شود.

## فعال‌سازی و دامنه

| حالت | فرمان | رفتار |
|---|---|---|
| پیش‌فرض | `SpiroLab.exe` | خواندن HLine فعال؛ «ارسال به سایت» با پیام «ارسال بالینی فعال نشده» متوقف می‌شود. |
| تست محدود | `SpiroLab.exe --hline-test=<NID>:<RECORD>` | فقط همان کد ملی و شناسهٔ پرونده. پروندهٔ تاریخی مجاز است، اما هویت دقیق، مرکز و مجوزها بررسی می‌شوند. |
| بالینی | `SpiroLab.exe --hline-write=all` | فقط پس از تأیید صریح مالک و پس از پاس‌شدن تست زنده. پیش‌فرض نیست. |

شناسهٔ تست در سورس hardcode نشده است. `SubmissionCoordinator` بدون مقصد
تعیین‌شده هیچ write انجام نمی‌دهد. در حالت تست، `person_id`، `person_id_no`،
`record id`، `admission_date` و `status` باید دقیقاً با draft یکی باشند.

## قرارداد شبکه

- `GET /api/auth/csrf` → `X-CSRF-Token` تازه پس از پذیرش نشست فایرفاکس.
- `GET /api/me` → شناسهٔ کاربر، مرکز و scopes.
- `GET /api/medical-records/{id}` → کنترل هویت/مرکز.
- `GET /api/medical-records/{id}/spirometry` → create در برابر edit/duplicate.
- `POST /api/medical-records/{id}/files` → multipart با `module_id=spirometry`،
  `medical_record_file` (JPG) و `desc`؛ پاسخ ۲۰۱ و `data.data.id`.
- `POST` یا `PATCH /api/medical-records/{id}/spirometry` → payload کامل.
- `GET /api/medical-records/{id}/files/spirometry` و
  `GET /api/medical-records/{id}/files/{fileId}/dl?inline=1` → بازخوانی فایل.

`test_date` به‌صورت **میلادی `YYYY/MM/DD`** فرستاده می‌شود.
`performed_by` شناسهٔ رشته‌ای کاربر واردشده است. `fev1_fvc_percent` جزء payload
نوشتن نیست؛ سرور آن را از مقادیر مشتق می‌کند. تمام فیلدهای برونکودیلاتور،
`srp` و `notes` برای گزارش PRE خالی (`null`) هستند. `vext` از قاعدهٔ
«بیشترین EVol واقعی کمتر از ۲۰۰ mL» می‌آید و صفرسازی نمی‌شود.

## ایمنی و حالت‌ها

- پیش از هر write: نشست، هویت شخص/پرونده، مرکز، scopes (`mrsp_u`, `mrsp_p`,
  `mrsp_nf`) و خالی‌بودن/نوع نتیجهٔ فعلی بررسی می‌شود.
- نتیجهٔ یکسان موجود → `AlreadyPresent`، بدون upload یا POST دوباره.
- نتیجهٔ متفاوت موجود → توقف با پیام تعارض؛ بازنویسی خودکار انجام نمی‌شود.
- `PATCH` فقط از مسیر صریح `SubmitUpdate` در دسترس است و دکمهٔ معمول آن را
  صدا نمی‌زند.
- `WriteAttempted` پیش از اولین درخواست غیر idempotent در journal ذخیره می‌شود.
- timeout/Create نامطمئن → `FailedOrUncertain`، بدون retry کورکورانه.
  `Recheck` فقط با GET نتیجه را تطبیق می‌دهد.
- upload نامطمئن → فهرست فایل‌ها و مقایسهٔ SHA-256 فایل‌های جدید؛ فقط یک
  تطبیق یکتا پذیرفته می‌شود، سپس همان `file_id` ادامه می‌یابد.
- `duplicate_record` → GET و تطبیق؛ یکسان → تأیید، متفاوت → توقف.
- موفقیت فقط پس از تطبیق اعداد (با دقت نمایش سرور)، تاریخ، شناسهٔ فایل و
  بایت‌های دقیق JPG اعلام می‌شود.
- خطای شبکه/عدم تطبیق، پیام کوتاه فارسی و حفظ پروندهٔ محلی می‌دهد.

## آزمون‌ها

`app\test.bat` اکنون هشت suite را اجرا می‌کند و مجموعاً **۶۳۵ بررسی** می‌دهد:

```
RegressionTests          76
StandaloneTests          73
PrecisionTests           66
ManeuverSelectionTests   59
ClinicalUiTests         166
HLinePhaseTests          26
HLineIntegrationTests   151
HLineWriteTests          18
```

`HLineWriteTests` یک سرور loopback واقعی (`HttpListener`) بالا می‌آورد و
مسیر کامل را با درخواست‌های HTTP واقعی آزمایش می‌کند: multipart و MIME،
پیلود و تاریخ میلادی، `performed_by`، نبود نسبت مشتق، VEXT، `null`های
اختیاری، نتیجهٔ یکسان، تعارض، 419 و CSRF تازه، upload timeout و
reconciliation، create timeout و Recheck، مرکز/مجوز نادرست، و متن آزاد
پزشک به‌صورت `Other` + `result_other`.

## تست زندهٔ مجاز روی پروندهٔ تعیین‌شده

مالک صریحاً تأیید کرد که پروندهٔ قدیمیِ سوخته برای تست انتخاب شده و نوشتن روی
آن مجاز است. دادهٔ اصلیِ همان پرونده از بکاپ محلی (صفحهٔ ذخیره‌شدهٔ HLine و JPG
اصلی) استخراج و از مسیر کامل `SubmissionDraft → LiveSubmissionGateway →
SubmissionCoordinator` بازگردانی شد:

- تاریخ انجام، FVC، FEV1، نسبت FEV1/FVC، PEF، FEF25-75 و VEXT دقیقاً مطابق
  بکاپ بودند؛ اعداد به‌دلیل PHI در این سند تکرار نمی‌شوند.
- تفسیر اصلی «Not recorded» بود؛ `result` و `result_other` عمداً `null`
  ماندند و هیچ دستهٔ Normal/Other جایگزین نشد.
- یک multipart upload و یک POST، پاسخ `201`، سپس بازخوانی `Confirmed`.
- فایل‌های پرونده از `0` به `1` رسید و JPG دانلودشده بایت‌به‌بایت با فایل
  بکاپ یکسان بود.
- اجرای دوباره با journal تازه `AlreadyPresent` داد، فایل‌ها `1` ماند و هیچ
  upload/POST دوباره‌ای انجام نشد.

پیش از نوشتن: نشست معتبر، هویت شخص/پرونده، مرکز، scopes و خالی‌بودن
spirometry بررسی شد. هیچ کوکی، توکن، کد ملی، نام یا شناسهٔ پرونده در این
مستند یا Git ثبت نشده است. جزئیات غیرحساس در [live-test-results.md](live-test-results.md)
است.

این نتیجه قرارداد را برای **همین پروندهٔ تعیین‌شده** تأیید می‌کند؛ نه برای
ارسال بالینی عمومی. `--hline-write=all` همچنان پیش‌فرض نیست و استفاده از آن
نیازمند تأیید صریح مالک است.

## ریسک‌های باقی‌مانده

1. مسیر `duplicate_record` در سرور واقعی آزموده نشد؛ تست زنده فقط حالت
   «نتیجهٔ از قبل موجود» را با GET و بدون write تأیید کرد.
2. allowlist سرور برای JPG در تست زنده پذیرفته شد (`201`)، اما سقف ۵ MiB و
   فایل‌های مرزی/خراب روی سرور واقعی آزموده نشدند.
3. `vext` به‌صورت mL پذیرفته و بازخوانی شد؛ رفتار سرور با مقادیر اعشاری/بزرگ‌تر
   و معنای واحد در سایر تنظیمات مرکز تأیید نشده است.
4. انتقال خودکار وضعیت module پس از POST به‌طور مستقل از UI بررسی نشده است.
5. مسیر `419`/CSRF تازه فقط با mock آزموده شده است؛ مصرف تک‌بارهٔ توکن در
   درخواست واقعی دیده نشد.
6. مجازبودن پروندهٔ تاریخی در حالت تست یک استثنای عمدی است؛ برای ارسال
   بالینی همیشه آخرین پرونده لازم است.
7. حذف فایل orphan پس از شکست Create پیاده نشده است؛ فایل باقی‌مانده در سرور
   ممکن است نیاز به پاک‌سازی دستی داشته باشد.
8. PATCH/اصلاح نتیجه، تعارض با نتیجهٔ متفاوت، timeout واقعی شبکه و
   بازخوانی پس از خطای نیمه‌کاره در محیط واقعی آزموده نشدند.
9. تست زنده با یک پروندهٔ تاریخی و یک حساب/مرکز انجام شد؛ تعمیم آن به
   همهٔ مراکز، نقش‌ها و نسخه‌های HLine اثبات نشده است.
10. تفسیرهای دیگر (`Obstructive`، `Restrictive`، severity و `Other`) فقط با
    mock آزموده شده‌اند؛ تست زنده عمداً `result=null` فرستاد.

## فایل‌های تغییرکرده

- `app/Src/HLine/HLineClient.cs` — کانال نوشتن محدود، CSRF تازه، هویت/مرکز/scopes،
  خواندن فایل و بازخوانی.
- `app/Src/HLine/LiveSubmissionGateway.cs` — payload، multipart، create/update،
  reconciliation و تطبیق.
- `app/Src/HLine/Submission.cs` — AlreadyPresent، تعارض، PATCH صریح، Recheck و
  تطبیق با دقت سرور.
- `app/Src/Ui/MainForm.cs` — اتصال یک‌کلیکی، دامنهٔ تست/تولید و پیام وضعیت.
- `app/Src/Ui/HLineForms.cs` — رفع مسابقهٔ فعال‌شدن دکمه پس از ورود.
- `app/Src/Ui/SpiroReport.cs` — دسترسی داخلی به متن تفسیر برای نگاشت، بدون تغییر ظاهر.
- `app/Src/Program.cs` — `--hline-test` و `--hline-write=all`.
- `app/Tests/HLineWriteTests.cs` — ۱۸ بررسی HTTP واقعی.
- `app/Tests/HLineIntegrationTests.cs` — انتظار AlreadyPresent به‌جای شکست برای نتیجهٔ یکسان.
- `app/build.bat`، `app/test.bat`، `README.md`، `docs/standalone/hline531-workflow.md`.
