# SpiroLab 5.4.6 — Records & HLine Reliability — RC2

## مبنا، اختیار و حفاظت

مبنای واقعی و آخرین remote شاخهٔ `feature/spirolab-clinical-best-values-layout` در شروع مأموریت، `ebb5dc03c58509560479e736b50e16fbcb0a4dc5` بود؛ اختلاف جدیدی وجود نداشت. checkout تمیز بود، پشتیبان source با `git archive` در `dist/Records-HLine-Source-Backup-*.zip` تهیه شد و شاخهٔ مستقل `feature/spirolab-records-hline-firefox` ساخته شد. مجوز مالک شامل Push همین شاخه و Pre-release است؛ merge یا force-push مجاز نیست. نسخهٔ محصول 5.4.6 و informational version این خروجی `5.4.6-records-hline-rc2` است.

مبنای آزموده‌شده در همین محیط: **1053 PASS / 0 FAIL / 10 SKIPPED**، لاگ `app/release-test/records-hline-baseline.log`. هیچ مانور واقعی، ورود خودکار با رمز، تغییر پروفایل Firefox یا نوشتن واقعی در HLine انجام نشده است.

## علت و اصلاح چهار بخش

| بخش | علت قابل اثبات در سورس قبلی | اصلاح |
|---|---|---|
| محل پرونده | `ExamStore.DataDirectory` همواره AppData/exams بود و به پوشهٔ اجرای Portable ارتباط نداشت. | records از `AppDomain.CurrentDomain.BaseDirectory` تعیین می‌شود؛ خودکار ایجاد می‌شود و خطای دسترسی به مسیر دیگری fallback نمی‌کند. راه بازیابی صریح در UI وجود دارد. |
| نام و روز بایگانی | `NewPath()` ساعت هر بار فراخوانی و شناسهٔ تصادفی را استفاده می‌کرد؛ زمان ایجاد پایدار در مدل نبود. | `Exam.CreatedAt` nullable برای سازگاری XML، زمان ثابت معاینهٔ تازه را نگه می‌دارد. `NewPath(Exam)` تاریخ انتخاب‌شدهٔ معاینه را با PersianCalendar به پوشهٔ yyyy-MM-dd و نام فارسی را به filename امن تبدیل می‌کند. برخوردها با -2/-3 حل می‌شوند. |
| خطای تکراری HLine | پیام برخورد، همهٔ نتیجه‌های غیرهمسانِ یک مقصد را یکسان بیان می‌کرد؛ نبود نتیجه فقط از یک endpoint استنباط می‌شد. parser قدیمی حتی پاسخ malformed را نبود داده تلقی می‌کرد. | parser فقط شکل معتبر object/null/empty و 404 را می‌پذیرد؛ placeholder خالی فقط با NoData و نبود فایل پذیرفته می‌شود. ناسازگاری ماژول، فایل بدون رکورد، نتیجهٔ موجود و journal نامطمئن جدا می‌مانند. هیچ reset خودکار journal یا بازنویسی اضافه نشده است. |
| پایداری Firefox | network status به Unavailable می‌رفت اما VerifySession فقط Connected را قبول می‌کرد. adoption می‌توانست jar و هویت قبلی را پاک کند. client مشترک قفل کل عملیات نشست نداشت و ورودِ بررسی‌شونده همزمان می‌توانست UI را منتظر شبکه نگه دارد. | درخواست‌های نشست/کوکی/CSRF سریال شده‌اند؛ Unavailable با همان jar از /me بازیابی می‌شود؛ GET منقضی یک بازیابی محدود دارد. هویت/مرکز محفوظ و هر تغییر نیازمند تأیید است. بررسی UI و ورود غیرمسدودکننده شدند. |

علت دقیق رخداد یک پروندهٔ واقعی بدون شناسه/پاسخ واقعی همان مقصد **تأیید نشده** است؛ پیام نقل‌شده به‌تنهایی مشخص نمی‌کند کدام وضعیت سرور رخ داده بود. اصلاح بر اساس نقاط قطعی سورس و سناریوهای mock انجام شده؛ وضعیت یک پروندهٔ بیمار حدس زده نشده است.

## ذخیره، بازیابی و کپی سوابق

- مسیر از CWD یا محل CMD گرفته نمی‌شود. نام از Patient.FullName است؛ حروف فارسی و نیم‌فاصله حفظ، نویسه‌های ویندوز/کنترلی و نام‌های رزروشده مدیریت می‌شوند. مسیرهای بیش از حد بلند به‌جای ذخیرهٔ ناقص رد می‌شوند.
- روز پوشه از ExaminationDate در اولین ذخیره و ساعت از CreatedAt است. تغییر ساعت رایانه پس از ایجاد، ساعت نام را عوض نمی‌کند. پس از اولین ذخیره، `_path` ثابت می‌ماند؛ ویرایش نام یا تاریخ در خود XML ثبت می‌شود، اما فایل بی‌صدا منتقل نمی‌شود. «ذخیره نسخهٔ بازیابی» کپی تازه با مسیر انتخابی می‌دهد و اصل را حذف نمی‌کند.
- DateTimeهای مدل، تاریخ گزارش و قرارداد تاریخ HLine میلادی/داخلی باقی مانده‌اند؛ فقط پوشه شمسی است. CreatedAt در snapshot عمیق و JSON schema-v2 به‌صورت فرادادهٔ اختیاری نیز حضور دارد.
- Save در همان پوشه به temp یکتا می‌نویسد و با File.Replace/File.Move اتمیک ثبت می‌کند. `.bak`، سقف 40 MiB، ممنوعیت DTD و اعتبارسنجی XML باقی‌اند. قبل از جایگزینی، شناسهٔ فایل موجود باید همان ExaminationId باشد؛ پروندهٔ مستقل هرگز روی آن نوشته نمی‌شود. در شکست، دادهٔ حافظه حفظ و UI امکان recovery دارد.
- ImportLegacy فقط با انتخاب کارشناس اجرا می‌شود. ابتدا شناسه‌های مقصد خوانده می‌شوند؛ مقصد خراب، کپی را قبل از جایگزینی متوقف می‌کند. سپس XML منبع با سقف/اعتبارسنجی قبلی خوانده و بایت‌به‌بایت از طریق temp کپی و با digest و شناسه تأیید می‌شود. اصل source حذف یا serialize دوباره نمی‌شود.
- همان شناسه و همان بایت‌ها skipped؛ همان شناسه با نسخهٔ متفاوت conflict/failed است. مشابهت نام به‌تنهایی تکرار محسوب نمی‌شود. تعداد imported/skipped/failed/conflicts در UI گزارش می‌شود. پروندهٔ منبع malformed شکست مستقل است.
- مدل تحلیلی، انتخاب مانورها، raw MIR، وضعیت ارسال و مسیرهای قدیمی export در XML کپی‌شده عیناً باقی‌اند. فایل‌های JPG/JSON جابه‌جا نمی‌شوند؛ اصل پوشه‌های خروجی را نگه دارید. مهاجرت به workflow5 انجام نمی‌شود؛ قواعد تاریخی load نسخه‌های قدیمی همان قواعد مبنا هستند.
- records، .spiro، temp، bak، logs و credentials در Git و بستهٔ انتشار نیستند. تنظیمات AppData جابه‌جا نشده‌اند.

نمونهٔ مصنوعی:

```text
SpiroLab.exe
records/1405-08-13/نمونه آزمایشی - 09-15-30.spiro
records/1405-08-13/نمونه آزمایشی - 09-15-30-2.spiro
records/1405-08-14/نمونه دیگر - 10-22-18.spiro
```

## تفکیک وضعیت HLine و محدودیت قرارداد

قرارداد مستند موجود در [hline-write-discovery.md](hline-write-discovery.md) و [hline53-contract.md](hline53-contract.md) یک object جاری برای `/medical-records/{id}/spirometry` و خطای `duplicate_record` در create دارد. برنامه این را یک slot جاری برای هر medical_record_id می‌داند؛ پشتیبانی از چند نتیجهٔ مستقل همزمان در همان slot اثبات نشده است. endpoint چندنتیجه‌ای، delete خودکار یا جایگزینی اجباری اختراع نشده است. history شخص با نتیجهٔ جاری مقصد یکی نیست.

| وضعیت | رفتار |
|---|---|
| همان شخص، Exam جدید، مقصد جدید | شناسهٔ شخص/کد ملی، آخرین پرونده، تاریخ/وضعیت، مرکز و scopes دوباره کنترل می‌شوند؛ journal معاینهٔ قبلی مانع نمی‌شود. |
| مقصد واقعاً خالی | object/null/404 معتبر، ماژول NoData و نبود فایل لازم‌اند؛ فقط journal تازه می‌تواند ارسال تازه کند. |
| حذف ظاهری وب و دادهٔ باقی‌ماندهٔ API | هنوز نتیجهٔ مقصد است؛ overwrite نمی‌شود. |
| API خالی ولی ماژول غیر NoData | `remote_state`؛ توقف برای بررسی سرور. |
| فایل قبلی بدون رکورد | `orphan_file`؛ نه آپلود تکراری، نه ساخت رکورد برای پوشاندن ابهام. |
| WriteAttempted همان Exam | Submit/Recheck فقط تطبیق خواندنی؛ نبود فعلی نتیجه مجوز تکرار عمل قبلی نیست. |
| همان داده و تصویر موجود | بدون آپلود/POST دوم، AlreadyPresent و تأیید module. |
| مقصد قدیمی/اشتباه یا پاسخ malformed | رد با علت مشخص؛ retarget پنهانی یا نبود دادهٔ ساختگی نداریم. |

شناسهٔ شخص، ExaminationId و medical_record_id سه چیز مستقل‌اند. NewExam دفترچهٔ تازه با reference یکتا می‌سازد؛ نام/زمان فایل کلید idempotency نیستند. اصل عدم تکرارِ نوشتن نامطمئن، تأیید exact JPG و DeviceProvenanceValidator حفظ شده‌اند. retry محدود CSRF پس از **رد صریح 419** همان رفتار قبلی است؛ timeout/network/نتیجهٔ نامعلوم اجازهٔ retry POST خودکار نمی‌دهند. 401/419 در خواندن می‌توانند با همان هویت بازاعتبارسنجی شوند؛ 403 به‌عنوان مجوز ناکافی باقی می‌ماند.

## نشست و هم‌زمانی

یک Monitor بازگشتی در HLineClient، عملیات مرکب احراز هویت و درخواست‌های آن را با عملیات همزمان lookup/probe هماهنگ می‌کند. ابتدا نشست معتبر موجود بررسی می‌شود؛ اگر معتبر باشد jar قدیمی Firefox روی آن نمی‌نشیند. خطای شبکه credential را حذف نمی‌کند. بازیابی خودکار فقط برای GET، حداکثر یک بار در هر درخواست و با فاصلهٔ حداقل ۳۰ ثانیه است؛ حداکثر سه profile در هر نوبت بررسی می‌شوند. بررسی دوره‌ای UI هر ۳۰ ثانیه و با یک worker فعال است؛ در حالت busy مانور/ارسال، نوبت دوره‌ای شروع نمی‌شود. درخواست غیرقابل‌تکرار وارد این retry نمی‌شود.

Candidate جدید با /me و user/medical-center قبلی مقایسه می‌شود. تولید واقعی HTTP در شکست adoption، jar و اطلاعات قبلی را بازمی‌گرداند. تغییر هویت/مرکز حالت واضح AuthenticationError/IdentityChangePending دارد و ارسال بسته می‌ماند؛ فقط انتخاب تعاملی «اتصال مجدد با فایرفاکس» و پاسخ Yes اجازهٔ پذیرش نشست جدیدِ اعتبارسنجی‌شده می‌دهد. مقصد بیمار پس از آن مستقل دوباره کنترل می‌شود. ورود با رمز/MFA صریح همچنان مسیر موجود است.

پروفایل تنظیم‌شده اول، سپس نشانهٔ قفل فعال، سپس default/آخرین access در اولویت‌اند. قفل فقط نشانه است؛ موفقیت login تنها با سرور ثابت می‌شود. خواندن live SQLite/WAL از قبل read-only بود و همان مسیر حفظ شده؛ آزمون تازه با writer باز، WAL و transaction ثبت‌نشده اضافه شد. expiry نامعتبر دیگر cookie جلسه فرض نمی‌شود. نه password store، نه تنظیم امنیتی و نه فایل پایگاه Firefox تغییر می‌کند؛ مقدار cookie/CSRF/password در log/settings/XML نوشته نمی‌شود.

## آزمون‌ها و خطاهای بررسی‌شده

اجرای کامل `app\test.bat`: **1101 PASS / 0 FAIL / 10 SKIPPED** در ۱۱ مجموعه؛ ۴۸ بررسی بیشتر از مبنای ۱۰۵۳. `build.bat` و `build.bat test` توسط runner اجرا شده‌اند. لاگ نهایی `app/release-test/records-hline-final.log` و جزئیات `app/test-logs/` هستند و به Git/Release پیوست نمی‌شوند. optional ضبط MIR ارائه نشده؛ skipped موفقیت نیست.

| مجموعه | PASS |
|---|---:|
| RegressionTests | 67 |
| StandaloneTests | 95 |
| PrecisionTests | 66 |
| ManeuverSelectionTests | 62 |
| ClinicalUiTests | 187 |
| MirBestValuesTests | 161 |
| RefinementTests | 78 |
| HLinePhaseTests | 25 |
| HLineIntegrationTests | 212 |
| HLineWriteTests | 49 |
| DeviceProvenanceTests | 99 |

پذیرش مستقل از پوشهٔ app: `release\MirBestValuesTests.exe mir-best-values-test-output --require-pending-end`: **162 PASS**. این‌ها mock دریافت هستند؛ موفقیت میدانی جدید ادعا نمی‌شود.

پوشش تازه شامل نام فارسی/نیم‌فاصله، rollover سال شمسی، زمان ثابت، برخورد/جلوگیری از overwrite، منع نوشتن واقعی ACL در پوشهٔ مصنوعی، شکست File.Replace و حفظ حافظه، bak، کپی دقیق و تعارض، دو Exam مستقل یک شخص، مقصد جدید، نبود واقعی، حالت template، فایل یتیم، API malformed، journal نامطمئن و بدون POST دوم، نشست معتبر با Firefox بسته، 401/419، نبود نشست، cooldown، 403، network/reconnect، تغییر user/center و تأیید صریح، درخواست‌های همزمان و WAL read-only است. آزمایش‌های قبلی چندپروفایل، انقضا، چرخش cookie و MFA هم باقی‌اند.

ممانعت جدید از overwrite شناسهٔ متفاوت، fixtureهای ثابتِ باقی‌مانده از اجرای قبل را رد می‌کرد؛ خروجی هر suite اکنون زیر run یکتا قرار می‌گیرد و دادهٔ قبلی حذف نمی‌شود. آزمون XML مخرب و آزمون زمان‌بندی نامعتبر دیگر عمداً یک فایل مشترک را overwrite نمی‌کنند. تست ورود برای completion واقعی async صبر می‌کند. خطای خواندن بایت DB باز در آزمون WAL با FileShare.ReadWrite رفع شد؛ مسیر read-only محصول عوض نشد. هیچ assertion قبلی حذف یا skip تازه‌ای اضافه نشده است.

## فایل‌ها، ساخت و حدود تأیید

کد تغییرکرده: `ExamStore.cs`، فرادادهٔ اختیاری `ExaminationSnapshot.cs`، `HLineClient.cs`، `FirefoxSession.cs`، اصلاح cleanup/comment در `Sqlite.cs`، `LiveSubmissionGateway.Read`، پیام‌های `SubmissionCoordinator.Submit`، `Ui/MainForm.cs`، `Ui/PatientForm.cs` و `Ui/HLineForms.cs`؛ همچنین informational version، تست‌ها، راهنما، gitignore و metadata بسته. فایل‌های MIR، PacketDecoder، BestValues، SessionBestValues، ManeuverAssessment، ClinicVext، ERS93، SpiroAnalysis، ChartPanel، DeviceProvenance و ResultExport تغییر نکرده‌اند.

Release واقعی x86 بدون simulator، test host، fixture، driver، records یا دادهٔ بیمار بسته‌بندی می‌شود. Source commit و SHA-256 در Version/manifest و فایل SHA کنار ZIP ثبت می‌شوند. نسخه‌ها و Releaseهای قبلی باقی می‌مانند؛ main/mir/test و شاخهٔ مبنا merge یا بازنویسی نمی‌شوند. راهنمای راه‌اندازی داخل ZIP است.

آزمون عملی باقی‌مانده: MIR واقعی و پایان طبیعی/دستی/لغو، Firefox/شبکه/Sleep واقعی، نشست واقعی چند مرکز، مقصد عملیاتی HLine با مجوز مستقل، داده‌های تاریخی مرکز پس از تهیهٔ پشتیبان، رایانهٔ پاک، DPI واقعی و BTPS. هیچ یک از این موارد از mock استنتاج نشده است. اتصال خودکار به معنی دور زدن MFA یا مجوز سرور نیست.
