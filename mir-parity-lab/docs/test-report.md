# گزارش آزمون فاز ۱

تاریخ اجرا: **۲ نوامبر ۲۰۲۶** (Asia/Tehran)
شاخه: `test`
مبنا: `4d02c8f401e1d6c5c8045debea23ced35a2ffbf3` (`mir`)
کامیت نهایی: در `docs/verification.md` ثبت شده است.

## ۱. خلاصه

| آزمون | نتیجه | تعداد |
|---|---|---:|
| ساخت Core + WinForms | PASS | ۳ مرحله |
| آزمون خودکار Core | **PASS** | **۲۹۸ بررسی، ۰ شکست** |
| UI smoke (پنجره واقعی WinForms) | **PASS** | **۹ بررسی، ۰ شکست** |
| اجرای واقعی `MIRParityLab.exe` | PASS | پنجره پایدار، عنوان `MIR Parity Lab` |
| آزمون سخت‌افزار/USB | انجام نشده | ۰ |
| آزمون MIR رسمی/SDK | انجام نشده | ۰ |

## ۲. محیط

- Windows: `Microsoft Windows NT 10.0.19045.0` (Windows 10 22H2).
- Compiler: `csc.exe` نسخه `4.0.30319.42000` از .NET Framework درون ویندوز.
- Target: x86، WinForms، .NET Framework 4.x.
- هیچ SDK، NuGet، اینترنت یا درایور MIR در build استفاده نشد.
- هیچ دستگاه USB در آزمون‌ها باز نشد.

## ۳. ساخت

```bat
cd mir-parity-lab
build.bat
```

خروجی:

```text
[1/3] Building ParityLab.Core.dll ...
[2/3] Building MIRParityLab.exe ...
[3/3] Build OK.
```

فایل‌های محلی:

```text
release\MIRParityLab.exe     56,832 bytes  SHA-256 6EB48FA5C151CAED58D520A94D677B3162F1DA927682B57DFB6DE6CF7C97037B
release\ParityLab.Core.dll               SHA-256 86AFC0C33E5730D7FF77EC8C5C9F85EF18DC9B7274B5823667BDB639088ACE4B
```

هش‌ها برای build محلی همین اجرا هستند؛ با تغییر سورس یا build مجدد ممکن است تغییر کنند.

## ۴. آزمون خودکار Core

```bat
test.bat
```

نتیجه:

```text
RESULT: 298 PASS, 0 FAIL
```

### پوشش آزمون

1. **بارگذاری fixture و SHA-256**
   - `test_id`، `data_origin=synthetic`، یک مانور، ۱۲۰ بسته.
   - حفظ SHA-256 فایل مبدأ.
   - `cod_LAST` ۹۶ بایتی و `cod_CURVE` ۱۷ بایتی خام.

2. **آفست‌ها و تبدیل `cod_LAST`**
   - `FVC @0x10`, `FEV1 @0x12`, `PEF @0x14`, `FET @0x1A`, `PEF time @0x50`.
   - `u16_be / 100` و واحدهای L، L/s، s.
   - کیفیت `0x0A` فقط خام و `Unknown`.
   - پوشش کامل بلوک: ۶ فیلد ۲ بایتی = ۱۲ بایت؛ بقیه Unknown.
   - عدم هم‌پوشانی و حفظ بایت‌به‌بایت بازه‌های Unknown.

3. **Fail-closed**
   - بلوک کوتاه: `minimum_length_met=false`, `block_valid=false`, فیلدهای حاضر `Unverified`.
   - انتساب stale: `association_capture_bound=false`, فیلدهای اصلی `Unverified`.
   - کیفیت حتی در بلوک stale همچنان `Unknown`.
   - `PEF time` خارج از محدوده `FET`: با وجود معتبر بودن بلوک اصلی، `Unverified` می‌ماند.

4. **بسته‌های زنده**
   - طول ۳ بایت.
   - `0x10` و `0x20` فقط به‌عنوان مشاهده سورس.
   - `0x04` به‌عنوان completion مشاهده‌شده.
   - نوع ناشناخته بدون حدس.

5. **بازسازی نرم‌افزاری**
   - ۱۲۰ نمونه، PEF تقریبی ۳.۰۰ L/s.
   - FVC/FEV1/ratio پر شده‌اند.
   - اصالت `Software-calculated`؛ هیچ کلید `device_reported` در مجموعه محاسبه‌شده نیست.

6. **ورود CSV رسمی**
   - ۶ ردیف، دقت، منبع، attestation و شاهد حفظ می‌شوند.
   - CSV quoting با کاما و quote دوتایی.

7. **حریم خصوصی**
   - `test_id` با فاصله/شبه‌هویت رد می‌شود.
   - کلید `patient_name` قبل از decode رد می‌شود.
   - متن `patient_name` در لاگ قبل از parse رد می‌شود.
   - ستون `address` در CSV رسمی رد می‌شود.

8. **واحدها**
   - `mL→L`، `L/min→L/s`، رد کردن dimension ناسازگار.

9. **مقایسه**
   - ۶ ردیف، ۶ فیلد decode‌شده.
   - FVC یکسان: `within_rounding_tolerance` با تلورانس ۰.۰۱.
   - `PEF time` جداگانه مقایسه می‌شود و بالینی نیست.
   - `FEV1/FVC`: `no_captured_value` با مقدار نرم‌افزاری جدا.
   - `test_id` تنها: `identity_not_established`.
   - FVC=4.40: `outside_rounding_tolerance`, اختلاف ۰.۰۸.
   - FVC=4320 mL: تبدیل و تلورانس ۱۰ mL، داخل تلورانس.
   - اتصال مقدار SpiroLab به ردیف رسمی همان پارامتر.
   - ردیف فقط SpiroLab: `no_mir_reference`.

10. **Export**
    - CSV: هدر + ۶ ردیف + ستون‌های اختلاف.
    - JSON: parse می‌شود؛ claim، maneuvers، decoded fields، unknown ranges، calculated provenance.
    - Markdown: عنوان، هشدار فارسی، اصالت.

11. **`.mir` opaque**
    - آرشیو علامت‌گذاری می‌شود.
    - هیچ مانوری حدس زده نمی‌شود.
    - blob خام حفظ می‌شود.
    - هشدار «not raw USB» تولید می‌شود.

## ۵. UI smoke

```bat
ui-smoke.bat
```

نتیجه:

```text
PASS  synthetic demo produces 6 comparison rows
PASS  comparison grid is bound to 6 rows
PASS  packet grid is bound to 120 rows
PASS  field grid is bound to 6 rows
PASS  comparison grid exposes parameter names
PASS  header, tab and status controls exist
PASS  tab starts below header
PASS  tab ends above status bar
PASS  tab has usable height
UI SMOKE RESULT: PASS
```

این آزمون یک پنجره واقعی WinForms می‌سازد، demo مصنوعی را بارگذاری می‌کند و اتصال Gridها و چیدمان Header/Tab/Status را بررسی می‌کند.

## ۶. اجرای برنامه

```bat
release\MIRParityLab.exe
```

نتیجه بررسی فرایند:

```text
Responding : True
HasExited  : False
Title      : MIR Parity Lab
Handle     : non-zero
```

## ۷. نقص‌های پیدا‌شده و اصلاح‌شده در همین فاز

1. **DataPropertyName اشتباه در Gridها**
   - نام ستون‌ها به‌صورت خودکار از header ساخته می‌شد و برای `Time (s)`, `Raw bytes`, `Official source`, `Rel diff %` و... به property واقعی نمی‌رسید.
   - اصلاح: نگاشت صریح header → property.

2. **هم‌پوشانی Tab با Header/Status**
   - `Dock=Fill` کلاینت را پر می‌کرد و Header/Status را می‌پوشاند.
   - UI smoke این را با bounds نشان داد.
   - اصلاح: z-order مناسب (`tabs.BringToFront()`), تأیید bounds:
     - Header: `Y=0..94`
     - Tab: `Y=94..868`
     - Status: `Y=868..893`

3. **اتصال SpiroLab فقط به ردیف‌های بعدی**
   - مقدار SpiroLab که بعد از ردیف رسمی وارد می‌شد، به ردیف رسمی attach نمی‌شد.
   - اصلاح: post-process بعد از ساخت همه ردیف‌ها.

4. **ناسازگاری fixture و انتظار آزمون**
   - `cod_CURVE` نمونه ۱۷ بایت بود، آزمون ۱۶ بایت انتظار داشت.
   - اصلاح: انتظار واقعی ۱۷ بایت.

5. **سازگاری .NET 4.x**
   - `File.ReadAllText(path, encoding, bufferSize)` در .NET 4.x موجود نبود.
   - `MaxJsonLength` از `long` به `int` cast شد.
   - overload مناسب برای `Strings(string[])` اضافه شد.

## ۸. آنچه آزمون نشده

- هیچ فرمان USB ارسال نشد.
- هیچ دستگاه MIR متصل نشد.
- هیچ firmware/calibration/settings/memory/record/serial/time تغییر نکرد.
- هیچ SDK رسمی اجرا نشد.
- هیچ `.mir` واقعی رمزگشایی نشد.
- هیچ مقایسه واقعی SpiroLab/MIR انجام نشد.
- BTPS، کیفیت مانور، کالیبراسیون و اعتبار بالینی آزمون نشدند.
- هیچ آزمون چنددستگاهی/چندfirmware انجام نشد.

## ۹. حکم آزمون

- پیاده‌سازی فاز ۱ از نظر ساخت، parser، decode مشاهده‌شده، محاسبه اختلاف، export و UI **قابل اجرا و آزمون‌شده** است.
- داده مصنوعی فقط plumbing را آزمون می‌کند.
- **هیچ ادعای برابری با MIR، اعتبار بالینی یا تأیید سخت‌افزاری در این گزارش وجود ندارد.**
