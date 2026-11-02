# SpiroLab 5.3.1 — پیاده‌سازی ارسال مستقیم به HLine

## خلاصه

مسیر «ارسال به سایت» اکنون یک پیاده‌سازی مستقیم C#/HTTP دارد. این مسیر از
مرورگر، Web2API یا import XML استفاده نمی‌کند. در نسخهٔ عادی، همان دکمه با
پروندهٔ جاریِ تأییدشدهٔ مراجع فعال است و به سوئیچ تست، پروندهٔ از پیش تعیین‌شده
یا فعال‌سازی جداگانه نیاز ندارد.

## دامنهٔ ارسال

| حالت | رفتار |
|---|---|
| اجرای عادی `SpiroLab.exe` | جست‌وجوی HLine، دریافت آخرین پروندهٔ مناسب مراجع، ثبت مانور، تولید JPG و «ارسال به سایت» فعال است. |
| ارسال | فقط پروندهٔ همان مراجعِ نمایش‌داده‌شده؛ پیش از write، هویت شخص/پرونده، مرکز، وضعیت و آخرین پرونده دوباره بررسی می‌شود. |
| ابزار تست داخلی | constructors تزریق gateway/پروندهٔ تست فقط برای suiteهای خودکار؛ هیچ سوئیچ عمومی یا وابستگی در EXE منتشرشده ندارد. |

شناسهٔ تست در سورس hardcode نشده است. `SubmissionCoordinator` در حالت عادی
مقصد را از gatewayِ تأییدشده می‌گیرد و هرگز بین بیماران جابه‌جا نمی‌شود.
`person_id`، `person_id_no`، `record id`، `admission_date` و `status` باید
دقیقاً با draft یکی باشند؛ در مسیر تست داخلی نیز همین تطبیق اجباری است.

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

`app\test.bat` اکنون هشت suite را اجرا می‌کند و مجموعاً **۶۴۴ بررسی** می‌دهد:

```
RegressionTests          76
StandaloneTests          73
PrecisionTests            66
ManeuverSelectionTests   59
ClinicalUiTests         166
HLinePhaseTests          26
HLineIntegrationTests   159
HLineWriteTests          19
```

`HLineWriteTests` یک سرور loopback واقعی (`HttpListener`) بالا می‌آورد و
مسیر کامل را با درخواست‌های HTTP واقعی آزمایش می‌کند: multipart و MIME،
پیلود و تاریخ میلادی، `performed_by`، نبود نسبت مشتق، VEXT، `null`های
اختیاری، نتیجهٔ یکسان، تعارض، 419 و CSRF تازه، upload timeout و
reconciliation، create timeout و Recheck، مرکز/مجوز نادرست، متن آزاد
پزشک به‌صورت `Other` + `result_other`، و **وضعیت ماژول `NoData` پس از
ذخیره به‌عنوان شکست قابل‌مشاهده، نه موفقیت**. مسیر GUI واقعی نیز با mock
محلی در [gui-e2e-results.md](gui-e2e-results.md) ثبت شده است (۲۷ بررسی،
بدون write زنده).

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
- پس از نوشتن، وضعیت ماژول اسپیرومتری در پروندهٔ واقعی `complete` خوانده شد؛
  بررسی خودکار همین وضعیت به مسیر موفقیت اضافه شده و `NoData` را رد می‌کند.

پیش از نوشتن: نشست معتبر، هویت شخص/پرونده، مرکز، scopes و خالی‌بودن
spirometry بررسی شد. هیچ کوکی، توکن، کد ملی، نام یا شناسهٔ پرونده در این
مستند یا Git ثبت نشده است. جزئیات غیرحساس در [live-test-results.md](live-test-results.md)
است.

این نتیجه قرارداد را برای **همین پروندهٔ تعیین‌شده** تأیید می‌کند؛ نه برای
ارسال بالینی عمومی. نسخهٔ عادی اکنون دکمهٔ ارسال را برای هر مراجعِ مجاز فعال
می‌کند، اما اعتبارسنجی زندهٔ یک پروندهٔ واقعیِ جدید و اعتبارسنجی بالینی/سخت‌افزاری
همچنان PENDING است.

## ریسک‌های باقی‌مانده

1. مسیر `duplicate_record` در سرور واقعی آزموده نشد؛ تست زنده فقط حالت
   «نتیجهٔ از قبل موجود» را با GET و بدون write تأیید کرد.
2. allowlist سرور برای JPG در تست زنده پذیرفته شد (`201`)، اما سقف ۵ MiB و
   فایل‌های مرزی/خراب روی سرور واقعی آزموده نشدند.
3. `vext` به‌صورت mL پذیرفته و بازخوانی شد؛ رفتار سرور با مقادیر اعشاری/بزرگ‌تر
   و معنای واحد در سایر تنظیمات مرکز تأیید نشده است.
4. وضعیت ماژول در پروندهٔ واقعی پس از نوشتن `complete` خوانده شد؛ گیت
   خودکار `NoData` فقط با mock آزموده شده و به‌عنوان یک write زندهٔ تازه
   اجرا نشده است.
5. مسیر `419`/CSRF تازه فقط با mock آزموده شده است؛ مصرف تک‌بارهٔ توکن در
   درخواست واقعی دیده نشد.
6. نسخهٔ عادی دیگر به پروندهٔ تست وابسته نیست، اما ارسال زندهٔ واقعی برای یک
   بیمار جدید در این مرحله انجام نشده است؛ اعتبارسنجی بالینی و MIR همچنان
   PENDING است. تست‌های خودکار فقط از mock و fixture ساختگی استفاده می‌کنند.
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
- `app/Src/Program.cs` — startup عادی بدون سوئیچ تست/تولید؛ فقط `--demo`/`--connect`/`--tab` تشخیصی.
- `app/Tests/HLineWriteTests.cs` — ۱۹ بررسی HTTP واقعی.
- `app/Tests/HLineIntegrationTests.cs` — انتظار AlreadyPresent به‌جای شکست برای نتیجهٔ یکسان.
- `docs/standalone/gui-e2e-results.md` — مسیر GUI واقعی با mock و ۲۷ بررسی.
- `app/build.bat`، `app/test.bat`، `README.md`، `docs/standalone/hline531-workflow.md`.
