# Firmware 4.8 compatibility — SpiroLab 5.4.6

## Scope and evidence

The existing direct MIRUSB transport, FVC command, packet decoding, capture association and cancellation rules remain in use. No vendor DLL or firmware is bundled, and no firmware-writing or calibration path was added.

Read-only identification on 2026-11-09 observed a restored Spirolab with logical PID 107, USB revision 1000, firmware 4.8, 36-byte SDA, device type 208, BigEndian (1), CodProt 1. Manufacturer communication protocol query 0x0A was unsupported; legacy cod_ON followed by the SDA bulk read worked. The production build's existing maximum-92-byte SDA read also returned this shorter block successfully. No patient examination, FVC start, calibration command or live HLine write was performed.

Offsets were checked against the locally installed manufacturer's `MirParser.MirSpirolabFamily.GetUsbAreaLastStructure` and `MirSpirolabATS19.GetUsbAreaLastStructure`, and identification against `ParserBase.GetDeviceInfo`. This is implementation evidence, not independent clinical validation.

| Field | 4.8 / type 208 | ATS2019 / type 241 |
|---|---:|---:|
| FVC | 14 | 16 |
| FEV1 | 16 | 18 |
| PEF | 18 | 20 |
| FET | 24 | 26 |
| PEFTIME | 74 | 80 |
| Uninterpreted quality word | 10 | 12 |

Core values retain their existing big-endian hundredth-unit conversion. The `LegacySpirolab` result field persists the layout through cloning and XML records; schema-v2 JSON exposes an additive `deviceResult.layout` label. Old records default to their original ATS2019 layout. Device provenance rechecks the same layout's raw core fields; no submission bypass was added. Measurement-source descriptions now carry the actual byte offsets.

## Verification

- Both production and isolated test builds succeeded.
- Eleven suites: **1128 PASS, 0 failed suites, 10 SKIPPED** without the optional external MIR capture (previous baseline 1101 PASS).
- Separate `--require-pending-end` acceptance: **182 PASS**.
- Added synthetic coverage: 36/92-byte SDA and fixed-width serial identification, both core layouts, PEFTIME/quality offsets, unsupported byte order/version, truncated SDA, frozen ownership, XML reopening, old-layout defaults, raw tampering and changed-layout rejection.
- Production `DeviceSession.Connect` identified the connected 4.8 device through direct MIRUSB; the temporary probe was not included in the release.

Live maneuver/output parity on 4.8 and repeat hardware validation on 8.2, calibration/BTPS, clean-PC and production HLine validation remain **PENDING**. The owner's report of successful device recovery is not application measurement validation. Raw device identification and manufacturer binaries are kept outside Git.

## تحویل کار و قدم‌های بعدی — ۲۰۲۶-۱۱-۰۹

شاخهٔ ادامهٔ کار: `feature/spirolab-firmware-4.8`؛ پایهٔ تغییرات `2f371cc` و commit پیاده‌سازی سازگاری `254c2db` است. شناسهٔ انتشار همچنان **5.4.6** است. راهنمای اپراتور در [firmware-48-fa.md](../operator/firmware-48-fa.md) قرار دارد.

### آنچه انجام شده است

- تشخیص قالب از SDA واقعی دستگاه: نوع ۲۰۸ با فریمور ۴٫۸ و نوع ۲۴۱ برای ATS2019. ترتیب بایت ناسازگار، بلوک شناسایی ناقص و فریمور ناشناختهٔ نوع ۲۰۸ رد می‌شوند.
- خواندن FVC، FEV1، PEF، FET، PEFTIME و کلمهٔ تشخیصی quality با محل صحیح هر قالب؛ قواعد range، پایان واقعی مانور، association و رد بلوک تکراری حفظ شده‌اند.
- ذخیرهٔ `LegacySpirolab` در پرونده و snapshot؛ برچسب `deviceResult.layout` در JSON نسخهٔ۲؛ استفاده از همان قالب در کنترل دادهٔ خام پیش از HLine و شرح منشأ مقادیر.
- تست‌های مصنوعی، build و بسته‌سازی گذشتند: ۱۱۲۸ PASS، صفر failure و ۱۰ SKIPPED؛ پذیرش جداگانهٔ pending-end برابر ۱۸۲ PASS. اتصال production به دستگاه واقعی ۴٫۸ فقط در حد شناسایی انجام شد.
- دستگاه، طبق گزارش مالک، پس از ریبوت رایانه و تکمیل آپدیت رسمی بازیابی شد. این گزارش با تأیید اندازه‌گیری برنامه برابر نیست. هیچ تغییر فریمور یا کالیبراسیونی از داخل پروژه انجام نشده است.

### ترتیب ادامهٔ کار

1. همین شاخه و ریشهٔ مخزن را استفاده کنید؛ پوشهٔ تودرتوی هم‌نام در آرشیو اولیه، کپی قدیمی است. `app\build.bat` و `run.bat` متعلق به ریشهٔ فعال هستند.
2. MIR Spiro و winspiroPRO را قبل از اتصال SpiroLab ببندید. یک دریافت واقعی با مشخصات مراجع کاملاً ساختگی، با حضور اپراتور انجام دهید؛ پایان طبیعی و پایان دستی را جداگانه بررسی کنید. این آزمون هنوز انجام نشده است.
3. مقدارها، واحدها، زمان‌بندی و منحنی همان دریافت را با خروجی مستقل MIR تطبیق دهید. اختلاف‌ها را همراه نسخهٔ فریمور، نوع دستگاه، گام نمونه‌برداری و طول بلوک LAST ثبت کنید. فقط به اتصال موفق یا fixture اکتفا نکنید؛ منشأ تأییدنشده را تأییدشده برچسب نزنید.
4. ذخیره و بازگشایی همان پرونده و تطابق JPG، JSON و دادهٔ خام را بررسی کنید. هر capture واقعی یا اطلاعات مراجع فقط محلی و خارج از Git بماند؛ برای regression فقط fixture مصنوعی بسازید.
5. شناسایی و دریافت واقعی ۸٫۲ را نیز دوباره بررسی کنید. خواندن کوتاه SDA با سقف ۹۲ بایت برای ۴٫۸ موفق بود؛ انتقال USB، BTPS یا کالیبراسیون را بدون شاهد تازه تغییر ندهید.
6. HLine را ابتدا با gateway مصنوعی بررسی کنید. هر نوشتن واقعی به بیمار، مجوز صریح جداگانه می‌خواهد؛ `DeviceProvenanceValidator` باید روی snapshot منجمد باقی بماند و bypass تولیدی اضافه نشود.
7. پس از هر تغییر منطق، `app\test.bat` و از پوشهٔ `app` دستور `release\MirBestValuesTests.exe mir-best-values-test-output --require-pending-end` را اجرا کنید. ضبط واقعی اختیاری است و SKIPPED به معنی PASS نیست. سپس `tools\package-release.ps1` بستهٔ بدون دادهٔ بیمار/درایور/فایل vendor می‌سازد.

### وضعیت خارج از مخزن روی رایانهٔ مالک

هنگام عیب‌یابی، واسط ثبت فرمان `C:\MIR\winspiroPRO\mirwsp.dll` با حفظ پشتیبان به `mirwsp.dll.capture-disabled` تغییر نام داده شد تا DLL اصلی سیستمی MIR بارگذاری شود. این تغییر در Git نیست؛ آن واسط را برای آزمون‌های بعدی خودکار برنگردانید. نصب رسمی winspiroPRO 8.5.7 نیز انجام شد. فایل‌های TSK، پشتیبان‌های نصب قبلی، لاگ‌ها و گزارش بازیابی خارج از مخزن و بستهٔ انتشار نگه داشته شدند؛ برای ساخت این پروژه نیاز نیستند.

اعتبارسنجی بالینی، مقایسهٔ مانور واقعی ۴٫۸/۸٫۲، BTPS، clean-PC و نوشتن تولیدی HLine همچنان **PENDING** هستند. تغییرات آینده پس از بررسی و آزمون محلی commit شوند؛ هیچ شاخه‌ای بدون مجوز صریح مالک در همان گفتگو push نشود.
