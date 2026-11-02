# جدول فیلدها و بایت‌های ناشناخته

مبنا: کد شاخه `mir` در کامیت `4d02c8f`، فایل‌های `DeviceSession.cs`، `PacketDecoder.cs`، `MirUsb.cs` و `BestValues.cs`. این جدول «شاهد سطح سورس» است، نه مشخصات تأییدشده MIR.

## ۱. فریم‌بندی

| مورد | مشاهده در سورس | وضعیت |
|---|---|---|
| `cod_LAST` = `0xA2` | ۴ بایت طول big-endian، سپس payload bulk | Observed source-level |
| `cod_CURVE` = `0xA3` | ۴ بایت طول big-endian، سپس payload bulk | Observed source-level |
| حداقل طول `cod_LAST` | `0x1C` بایت | Observed; reject below |
| حداکثر طول بلوک | `200000` بایت | Source guard |
| بسته زنده | ۳ بایت: `[type][raw_hi][raw_lo]` | Observed |
| `raw` | `u16` big-endian | Observed |
| شروع FVC | `Get(0x08, 3)`؛ بایت اول `0x30` یا `0x40`؛ مقدار `u16_be` | Observed |
| حالت گام | `0x30`: حجم/گام = value/10000 L؛ `0x40`: زمان/گام = value/1000 s | Observed |
| حالت آماده | `Get(0xFF,1)`؛ `0x00` = ready و `0xFF` = no response/unavailable | Observed |
| شناسه | `cod_ON` (`0x00`) + خواندن ۹۲ بایت SDA | Observed; write command, not used by this tool |

## ۲. فیلدهای `cod_LAST`

| کلید | آفست | اندازه | تبدیل | واحد | حداقل طول | اصالت پیش‌فرض | استفاده بالینی |
|---|---:|---:|---|---|---:|---|---|
| `FVC` | `0x10` | 2 | `u16_be / 100` | L | `0x12` | Device-reported اگر capture-bound | بله، فقط با بلوک معتبر |
| `FEV1` | `0x12` | 2 | `u16_be / 100` | L | `0x14` | Device-reported اگر capture-bound | بله، فقط با بلوک معتبر |
| `PEF` | `0x14` | 2 | `u16_be / 100` | L/s | `0x16` | Device-reported اگر capture-bound | بله، فقط با بلوک معتبر |
| `FET` | `0x1A` | 2 | `u16_be / 100` | s | `0x1C` | Device-reported اگر capture-bound | بله، فقط با بلوک معتبر |
| `PEF_TIME` | `0x50` | 2 | `u16_be / 100` | s | `0x52` | Device-reported اگر capture-bound | **خیر**؛ مبدأ زمان و معنا تأیید نشده |
| `QUALITY_RAW` | `0x0A` | 2 | `u16_be` خام | — | `0x0C` | **Unknown** | **هرگز** |

### محدوده‌های sanity در سورس اصلی

این محدوده‌ها فقط برای رد کردن بلوک مخدوش/غیرمنتظره در `DeviceSession.ReadResults` هستند و **اعتبار بالینی** نمی‌سازند:

- `FVC`: `0.05 .. 12` L
- `FEV1`: `0.05 .. FVC + 0.05` L
- `PEF`: `> 0 .. 20` L/s
- `FET`: `> 0 .. 30` s
- `PEF time`: `> 0 .. FET + 0.05` s، فقط اگر طول ≥ `0x52`

## ۳. بایت‌های ناشناخته

در بلوک نمونه `0x60` بایتی، بازه‌های زیر Unknown هستند و فقط hex آن‌ها حفظ می‌شود:

| آفست | اندازه | تفسیر |
|---|---:|---|
| `0x00` | 10 | Unknown |
| `0x0C` | 4 | Unknown |
| `0x16` | 4 | Unknown |
| `0x1C` | تا قبل از `0x50` | Unknown |
| `0x52` | تا انتهای بلوک | Unknown |

قواعد:

- هیچ بایتی حذف نمی‌شود.
- هیچ بازه‌ای با بازه دیگر هم‌پوشانی ندارد.
- مجموع «فیلدهای شناخته‌شده + بازه‌های ناشناخته» باید کل بلوک را بپوشاند.
- کیفیت/رزرو `0x0A` عدد خام است؛ نه نمره ATS/ERS، نه کیفیت قابل استفاده بالینی.
- هیچ حدس معنایی برای بایت‌های ناشناخته ثبت نمی‌شود.

## ۴. بسته‌های زنده

| نوع | نام در کاتالوگ | مشاهده | وضعیت |
|---:|---|---|---|
| `0x10` | Live signal packet | `PacketDecoder` آن را flow-like می‌گیرد | Observed source-only |
| `0x20` | Timed-volume packet | در حالت زمان‌محور volume-like | Observed source-only |
| `0x01` | Live packet type | در حلقه capture پذیرفته می‌شود اما decode نمی‌شود | Unknown |
| `0x04` | Completion marker | پایان طبیعی capture | Observed source-only |
| `0xFF` | No new sample | نادیده گرفته می‌شود؛ اندازه‌گیری نیست | Observed source-only |

هر نوع ناشناخته با hex کامل نمایش داده می‌شود و هیچ عددی از آن ساخته نمی‌شود.

## ۵. تبدیل علامت در `PacketDecoder.cs`

فرمول سورس برای جریان:

```text
(raw < 0x8000 ? raw : raw - 65535) / 100.0
```

این فرمول با مکمل دو استاندارد (`raw - 65536`) یک واحد اختلاف دارد. ابزار این اختلاف را «شاهد سورس» ثبت می‌کند و آن را به‌عنوان فرمول تأییدشده MIR اعلام نمی‌کند. برای مقادیر مثبت، اختلافی ندارد. برای مقادیر منفی، تا روشن‌شدن معنای دقیق پروتکل، مقدار باید با احتیاط بررسی شود.

## ۶. `cod_CURVE`

- تنها به‌صورت خام ذخیره می‌شود.
- در سورس بررسی‌شده decoder وجود ندارد.
- ابزار آن را با `cod_LAST` یکی نمی‌گیرد.
- بازسازی نرم‌افزاری از بسته‌های زنده، **جایگزین** منحنی ذخیره‌شده دستگاه نیست.

## ۷. انتساب مانور

هیچ شناسه مانور در پروتکل مشاهده نشد. شواهد مورد استفاده:

1. توالی capture در همان session.
2. تکمیل طبیعی با `0x04`.
3. خواندن فوری و واحد پس از تکمیل (پنجره ۱۰ ثانیه در سورس).
4. همان سریال دستگاه.
5. رد بلوک byte-identical تکراری.

ضعف‌ها:

- بلوک قدیمی که byte-identical نباشد، از خود بلوک قابل تشخیص نیست.
- چند دستگاه/نرم‌افزار هم‌زمان می‌توانند داده را جابه‌جا کنند.
- `cod_LAST` فاقد شناسه مانور در سطح دستگاه است.
- بنابراین انتساب در ابزار «قابل‌اثبات با شاهد ورودی» است، نه اثبات سخت‌افزاری.

## ۸. مرجع کد

| موضوع | فایل | خطوط تقریبی |
|---|---|---:|
| شروع FVC و گام | `app/Src/Device/DeviceSession.cs` | 192–220 |
| بسته زنده | `app/Src/Device/DeviceSession.cs` | 228–239 |
| حلقه capture و پایان | `app/Src/Device/DeviceSession.cs` | 259–285 |
| خواندن و اعتبارسنجی `cod_LAST` | `app/Src/Device/DeviceSession.cs` | 315–414 |
| `cod_CURVE` | `app/Src/Device/DeviceSession.cs` | 418–427 |
| فریم طول‌دار | `app/Src/MirUsb.cs` | 426–444 |
| فرمول جریان | `app/Src/Device/PacketDecoder.cs` | 20–39 |
| نگاشت مقادیر دستگاه | `app/Src/BestValues.cs` | 137–157 |
