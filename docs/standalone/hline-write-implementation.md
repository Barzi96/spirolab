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

`app\test.bat` اکنون هشت suite را اجرا می‌کند و مجموعاً **۶۳۴ بررسی** می‌دهد:

```
RegressionTests          76
StandaloneTests          73
PrecisionTests           66
ManeuverSelectionTests   59
ClinicalUiTests         166
HLinePhaseTests          26
HLineIntegrationTests   151
HLineWriteTests          17
```

`HLineWriteTests` یک سرور loopback واقعی (`HttpListener`) بالا می‌آورد و
مسیر کامل را با درخواست‌های HTTP واقعی آزمایش می‌کند: multipart و MIME،
پیلود و تاریخ میلادی، `performed_by`، نبود نسبت مشتق، VEXT، `null`های
اختیاری، نتیجهٔ یکسان، تعارض، 419 و CSRF تازه، upload timeout و
reconciliation، create timeout و Recheck، مرکز/مجوز نادرست، و متن آزاد
پزشک به‌صورت `Other` + `result_other`.

## اعتبارسنجی فقط‌خواندنی روی سامانهٔ واقعی

با نشست فایرفاکس و بدون هیچ write، موارد زیر بررسی شد:

- نشست معتبر و `GET /api/me` موفق؛
- کد ملی و پروندهٔ معرفی‌شده در سامانه وجود دارند و هویت شخص/پرونده تأیید شد؛
- پرونده به مرکز کاربر تعلق دارد و scopes لازم (`mrsp_u/p/i/nf`) موجودند؛
- بخش spirometry خالی است و module آن وجود دارد؛
- پرونده در فهرست شش‌گانهٔ بیمار **آخرین پرونده نیست** و هیچ نشانهٔ تست
  (نام/برچسب) ندارد.

به همین دلیل، چون شناسه‌ها به یک پروندهٔ واقعی‌نما اشاره می‌کردند و
«پروندهٔ تست اختصاصی» بودن آن‌ها قابل تأیید نبود، **هیچ write زنده‌ای انجام
نشد**. این تصمیم مطابق قید صریح عدم نوشتن روی پرونده‌ای است که ممکن است
جای‌نگهدار باشد. هیچ کوکی، توکن، کد ملی، نام یا مقدار بالینی در این گزارش
ثبت نشده است.

## ریسک‌های باقی‌مانده

1. وضعیت واقعی HTTP برای `duplicate_record` (فرض ۴۲۲) و پیام‌های validation.
2. allowlist سمت سرور برای MIME/پسوند JPG و سقف ۵ MiB.
3. واحد `vext` (mL فرض شده) و رفتار سرور با مقادیر گردشده.
4. اینکه POST/PATCH وضعیت module را خودکار به غیر از `NoData` تغییر می‌دهد.
5. عمر/تک‌مصرفی CSRF؛ مسیر 419 فقط با mock آزموده شده است.
6. مجازبودن پروندهٔ تاریخی در حالت تست؛ برای ارسال بالینی همیشه آخرین پرونده
   لازم است.
7. حذف فایل orphan پس از شکست Create پیاده نشده است؛ فایل باقی‌مانده در سرور
   ممکن است نیاز به پاک‌سازی دستی داشته باشد.

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
- `app/Tests/HLineWriteTests.cs` — ۱۷ بررسی HTTP واقعی.
- `app/Tests/HLineIntegrationTests.cs` — انتظار AlreadyPresent به‌جای شکست برای نتیجهٔ یکسان.
- `app/build.bat`، `app/test.bat`، `README.md`، `docs/standalone/hline531-workflow.md`.
