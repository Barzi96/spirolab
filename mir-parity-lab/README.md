# MIR Parity Lab — فاز ۱

ابزار مستقل، آفلاین و فقط‌خواندنی برای پژوهش روی بسته‌های USB دستگاه MIR Spirolab و مقایسه شواهد رسمی MIR.

> **این ابزار `SpiroLab.exe` نیست.** هیچ فرمان USB ارسال نمی‌کند، هیچ درایوری را باز نمی‌کند و هیچ نوشتنی روی دستگاه انجام نمی‌دهد. در این فاز هیچ ادعایی درباره برابری واقعی با MIR، اعتبار بالینی، BTPS یا کالیبراسیون مطرح نمی‌شود.

- شاخه: `test` (ایزوله از `mir`)
- نقطه شروع: `4d02c8f` — آخرین کامیت `mir`
- خروجی اجرایی: `release\MIRParityLab.exe`
- کتابخانه: `release\ParityLab.Core.dll`
- آزمون: `release\ParityLab.Tests.exe`
- داده نمونه: `fixtures\` — کاملاً **مصنوعی**

## ۱. قابلیت‌ها

- ورود capture بومی JSON، لاگ متنی دستگاه، فایل بسته hex و فایل opaque (`.bin` / `.mir`).
- نمایش طول، نوع، زمان، مقدار خام و hex هر بسته.
- رمزگشایی فقط فیلدهای مشاهده‌شده `cod_LAST`:
  - `FVC` در `0x10`، `u16_be / 100`، واحد L
  - `FEV1` در `0x12`، `u16_be / 100`، واحد L
  - `PEF` در `0x14`، `u16_be / 100`، واحد L/s
  - `FET` در `0x1A`، `u16_be / 100`، واحد s
  - `PEF time` در `0x50`، فقط اگر طول بلوک ≥ `0x52`، واحد s
  - `quality/reserved` در `0x0A`، فقط به‌عنوان عدد خام و با وضعیت **Unknown**
- حفظ بایت‌های ناشناخته، بدون حدس معنا یا استفاده بالینی.
- نمایش هم‌زمان مقدار خام (u16/hex) و مقدار تبدیل‌شده.
- ورود مقادیر رسمی MIR از CSV یا ورود دستی.
- تطبیق با `test_id` غیرهویتی و شواهد مانور (کلید مانور، ترتیب، PRE/POST، زمان، تأیید اپراتور).
- محاسبه اختلاف مطلق و نسبی، با تلورانس گردکردن مستند.
- خروجی CSV، JSON و گزارش پژوهشی Markdown فارسی.
- مقدار نرم‌افزاری مستقل از بسته‌های زنده، با برچسب `Software-calculated` و صراحتاً **نه SpiroLab و نه MIR**.

## ۲. ساخت و اجرا

نیازمندی: ویندوز و .NET Framework 4.x؛ کامپایل با `csc.exe` داخلی ویندوز، بدون SDK.

```bat
cd mir-parity-lab
build.bat
run.bat
```

یا مستقیم:

```bat
release\MIRParityLab.exe
```

اجرای آزمون‌ها:

```bat
test.bat
ui-smoke.bat
```

`test.bat` آزمون Core و `ui-smoke.bat` آزمون پنجره واقعی WinForms را اجرا می‌کند.

ساخت مجدد نمونه‌های مصنوعی (اختیاری):

```bat
python tools\make_synthetic_fixture.py
```

## ۳. گردش کار سریع

1. `Load synthetic demo` را بزنید تا رابط، قرارداد داده و خروجی‌ها بدون داده واقعی آزمایش شوند.
2. برای داده واقعی، `Load capture...` را بزنید. فایل لاگ یا JSON بومی را انتخاب کنید.
3. در تب `Official MIR evidence`، فایل CSV رسمی را وارد کنید یا `Add manual value...` را بزنید.
4. `Recalculate comparison` را بزنید.
5. خروجی‌های CSV، JSON و گزارش فارسی را ذخیره کنید.
6. قبل از هر تفسیر، بخش «حدود اثبات» در گزارش را بخوانید.

## ۴. قالب capture بومی

حداقل ساختار:

```json
{
  "schema": "mir-parity-capture/v1",
  "test_id": "SYN-STRUCT-001",
  "data_origin": "synthetic",
  "capture": {
    "recorded_at": "2026-01-15T09:00:00Z",
    "device_model": "SYNTHETIC fixture — not a device",
    "device_serial": "",
    "notes": "synthetic only"
  },
  "maneuvers": [
    {
      "maneuver_key": "SYN-STRUCT-001-M1",
      "sequence": 1,
      "pre_post": "PRE",
      "recorded_at": "2026-01-15T09:00:00Z",
      "association": "capture_bound_immediate_single_read",
      "association_evidence": "...",
      "step_type": "0x40",
      "step": 0.02,
      "cod_last_hex": "....",
      "cod_curve_hex": "....",
      "packets": [
        {"index": 0, "seconds": 0.02, "type": "0x10", "raw": 30, "hex": "10001E"}
      ]
    }
  ]
}
```

`data_origin` یکی از `synthetic`، `live_deidentified` یا `unknown` است. برای فاز ۱، نمونه‌ها باید `synthetic` باشند. کلیدهای شناسایی بیمار مانند `patient_name`، `national_id`، `mrn`، `birth_date`، `address`، `phone` و `email` پذیرفته نمی‌شوند.

ورودی‌های دیگر:

| پسوند | رفتار |
|---|---|
| `.log`, `.txt` | استخراج `packet ...s TYPE RAW`، `cod_LAST ... bytes: HEX`، `test_id=` و `association=` |
| `.hex`, `.pkts` | هر خط یک بسته سه‌بایتی، مثل `10 1388` یا `101388` |
| `.bin` | یک blob opaque؛ هیچ فریمی حدس زده نمی‌شود |
| `.mir` | آرشیو اختصاصی opaque؛ **هرگز معادل داده خام USB فرض نمی‌شود** و رمزگشایی نمی‌شود |

## ۵. قالب CSV مقادیر رسمی MIR

ستون‌های الزامی:

```text
test_id, parameter, value, unit, source
```

ستون‌های توصیه‌شده:

```text
maneuver_key, sequence, pre_post, recorded_at,
precision, evidence, same_maneuver_attested, notes
```

مقادیر مجاز `source`:

- `mir_device_display`
- `mir_device_print`
- `mir_software`
- `spirolab_software`
- `manual_reference`
- `reference_predicted`

`precision` دقت نمایش/گردکردن خروجی رسمی است، نه خطای دستگاه. اگر وارد نشود، ابزار «تلورانس قابل‌اثبات نیست» را گزارش می‌کند و عدد را با برچسب `tolerance_not_established` نگه می‌دارد.

نمونه کامل: `fixtures\synthetic-official.csv`. قالب خالی از دکمه `Save CSV template` در برنامه قابل ذخیره است.

## ۶. قواعد تطبیق و اختلاف

- `test_id` تنها، اثبات «همان مانور» نیست.
- تطبیق فقط با یکی از این شواهد برقرار می‌شود:
  - `maneuver_key` دقیق + PRE/POST سازگار + زمان در بازه ۳۰۰ ثانیه؛ یا
  - `same_maneuver_attested=yes` با کلید/تک‌مانور/ترتیب سازگار.
- در غیر این صورت، اعداد برای بررسی پژوهشی نمایش داده می‌شوند اما وضعیت `identity_not_established` است و اختلاف، ادعای برابری نیست.
- تلورانس = نصف دقت خروجی رسمی + نصف تفکیک‌پذیری مشاهده‌شده دستگاه.
- تکرارپذیری بین‌مانوری ATS/ERS به‌عنوان خطای مجاز دو نرم‌افزار در همان مانور استفاده **نمی‌شود**.
- اختلاف خارج از تلورانس، علت‌یابی قطعی ندارد؛ ستون علت ممکن، «تعیین‌نشده» است.

## ۷. وضعیت اصالت داده

هر مقدار یکی از این وضعیت‌ها را دارد:

| وضعیت | در این ابزار |
|---|---|
| `Device-reported` | فقط فیلد مستقیم `cod_LAST` با بلوک معتبر و انتساب capture-bound |
| `Device-derived` | در این فاز هیچ فیلدی به‌عنوان اثبات‌شده استفاده نمی‌کند |
| `Software-calculated` | بازسازی مستقل Parity Lab از بسته‌ها؛ نه SpiroLab و نه MIR |
| `Reference-predicted` | فقط اگر کاربر با همین منبع وارد کند؛ در مقایسه اندازه‌گیری قرار نمی‌گیرد |
| `Unknown` | فیلد quality/reserved و همه بایت‌های ناشناخته |
| `Unverified` | مقدار خوانده‌شده بدون انتساب/اعتبار کافی |

هیچ مقدار نرم‌افزاری با برچسب `Device-reported` گزارش نمی‌شود. هیچ فیلد Unknown در محاسبه بالینی استفاده نمی‌شود.

## ۸. ایمنی

این نسخه هیچ کد USB، `DeviceIoControl`، بازکردن درایور یا ارسال فرمان ندارد. بنابراین:

- Firmware، calibration، settings، memory، records، serial، communication settings، time و date قابل تغییر نیستند.
- هیچ نوشتن، پاک‌کردن یا به‌روزرسانی دستگاه وجود ندارد.
- برای دریافت زنده، در فاز ۲ باید فقط رفتارهای اثبات‌شده و با دسترسی انحصاری USB استفاده شوند.

## ۹. ساختار پوشه

```text
mir-parity-lab/
  src/Core/        موتور capture، decode، مقایسه و export
  src/App/         رابط WinForms
  tests/           آزمون خودکار ۲۹۸ بررسی
  fixtures/        JSON/CSV مصنوعی
  samples/         خروجی نمونه CSV/JSON/Markdown
  docs/            شواهد پروتکل، دسترسی رسمی، راهنما و چک‌لیست
  build.bat
  test.bat
```

## ۱۰. اسناد کلیدی

- [راهنمای عملیاتی فارسی](docs/operator-guide-fa.md)
- [جدول فیلدها و بایت‌های ناشناخته](docs/field-table.md)
- [شواهد پروتکل و کد مبنا](docs/protocol-evidence.md)
- [وضعیت مستندات و SDK رسمی MIR](docs/official-access.md)
- [چک‌لیست جمع‌آوری داده](docs/data-collection-checklist.md)
- [گزارش آزمون و محدودیت‌ها](docs/test-report.md)
- [تأیید ایزوله‌سازی شاخه](docs/verification.md)
- [محدودیت‌های اثبات‌نشده](docs/limitations.md)

## ۱۱. خروجی فاز ۱

- برنامه مستقل `MIR Parity Lab`.
- سورس جداگانه در `test`.
- ورود و بررسی بسته خام.
- جدول فیلد شناخته‌شده/ناشناخته.
- ورود رسمی MIR از CSV/دستی.
- خروجی CSV/JSON و گزارش فارسی.
- آزمون خودکار و نمونه‌های مصنوعی.
- عدم ادعای برابری با MIR تا دریافت داده واقعی و شواهد جفت‌شده.
