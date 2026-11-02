# گزارش پژوهشی MIR Parity Lab — فاز ۱

> **هشدار روش‌شناختی:** این گزارش ادعایی درباره برابری واقعی با MIR، اعتبار بالینی، کالیبراسیون یا BTPS نمی‌کند. فاز ۱ فقط برای بررسی ساختار بسته، منشأ داده و محاسبه اختلاف است.

- شناسه آزمایش غیرهویتی: `SYN-STRUCT-001`
- منشأ داده: `synthetic`
- زمان ثبت: `2026-01-15T09:00:00.0000000Z`
- مدل دستگاه: `SYNTHETIC fixture — not a device`
- فایل مبدأ: `synthetic-capture.json`
- SHA-256 فایل مبدأ: `C7830EAC5EAA21429178CCDBA2F030219D17D98067E2BAFF66C1C7D275AFFA17`
- زمان تولید گزارش (UTC): `2026-11-02T17:52:36.6875068Z`
- تعداد ردیف مقایسه: **6**

## خلاصه اجرایی

- داخل تلورانس گردکردن: **5**
- خارج از تلورانس گردکردن: **0**
- فاقد شواهد کافی برای همان مانور: **0**
- فاقد مقدار رسمی MIR: **0**
- سایر/غیرقابل‌مقایسه: **1**

> داده این اجرا **مصنوعی** است. ردیف‌های این گزارش نتیجه MIR نیستند.

## مانور `SYN-STRUCT-001-M1`

- ترتیب: 1
- PRE/POST: `PRE`
- association: `capture_bound_immediate_single_read`
- شاهد انتساب: SYNTHETIC fixture metadata; not a real capture token.
- طول cod_LAST: 96 بایت
- طول cod_CURVE: 17 بایت (فقط ذخیره خام)
- تعداد بسته زنده: 120

### فیلدهای رمزگشایی‌شده

| فیلد | آفست | اندازه | خام | مقدار | واحد | تبدیل | منشأ | وضعیت |
|---|---:|---:|---|---:|---|---|---|---|
| FVC | 0x10 | 2 | `01 B0` | 4.32 | L | u16_be / 100 | Device-reported | Direct cod_LAST field, capture-bound by the supplied association evidence. Device identity is still not a device-level maneuver ID. |
| FEV1 | 0x12 | 2 | `01 5F` | 3.51 | L | u16_be / 100 | Device-reported | Direct cod_LAST field, capture-bound by the supplied association evidence. Device identity is still not a device-level maneuver ID. |
| PEF | 0x14 | 2 | `03 6C` | 8.76 | L/s | u16_be / 100 | Device-reported | Direct cod_LAST field, capture-bound by the supplied association evidence. Device identity is still not a device-level maneuver ID. |
| FET | 0x1A | 2 | `02 82` | 6.42 | s | u16_be / 100 | Device-reported | Direct cod_LAST field, capture-bound by the supplied association evidence. Device identity is still not a device-level maneuver ID. |
| PEF_TIME | 0x50 | 2 | `00 0C` | 0.12 | s | u16_be / 100 | Device-reported | Direct cod_LAST field, capture-bound by the supplied association evidence. Device identity is still not a device-level maneuver ID. |
| QUALITY_RAW | 0x0A | 2 | `00 01` | 1 |  | u16_be (raw integer; NO interpretation) | Unknown | The raw integer is preserved. No clinical, ATS/ERS or device-quality meaning is assigned. |

### بایت‌های ناشناخته (بدون حدس معنا)

| آفست | اندازه | خام | منشأ |
|---|---:|---|---|
| 0x00 | 10 | `A5 A5 A5 A5 A5 A5 A5 A5 A5 A5` | Unknown |
| 0x0C | 4 | `A5 A5 A5 A5` | Unknown |
| 0x16 | 4 | `A5 A5 A5 A5` | Unknown |
| 0x1C | 52 | `A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5` | Unknown |
| 0x52 | 14 | `A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5 A5` | Unknown |

### مقایسه

| پارامتر | رسمی | منبع رسمی | خوانده‌شده | منشأ خوانده‌شده | نرم‌افزاری | اختلاف رسمی-خوانده | اختلاف رسمی-نرم‌افزار | وضعیت نرم‌افزاری | تلورانس | وضعیت | دلیل |
|---|---:|---|---:|---|---:|---:|---:|---|---:|---|---|
| FVC | 4.32 L | mir_device_print | 4.32 | Device-reported | 3.5946 | 0 | 0.7254 | outside_rounding_tolerance | 0.01 | within_rounding_tolerance | Absolute difference is within the combined half-resolution rounding tolerance. |
| FEV1 | 3.51 L | mir_device_print | 3.51 | Device-reported | 2.2986 | 0 | 1.2114 | outside_rounding_tolerance | 0.01 | within_rounding_tolerance | Absolute difference is within the combined half-resolution rounding tolerance. |
| PEF | 8.76 L/s | mir_device_print | 8.76 | Device-reported | 3 | 0 | 5.76 | outside_rounding_tolerance | 0.01 | within_rounding_tolerance | Absolute difference is within the combined half-resolution rounding tolerance. |
| FET | 6.42 s | mir_device_print | 6.42 | Device-reported | 2.32 | 0 | 4.1 | outside_rounding_tolerance | 0.01 | within_rounding_tolerance | Absolute difference is within the combined half-resolution rounding tolerance. |
| PEF time | 0.12 s | mir_device_print | 0.12 | Device-reported | 0.18 | 0 | 0.06 | outside_rounding_tolerance | 0.01 | within_rounding_tolerance | Absolute difference is within the combined half-resolution rounding tolerance. |
| FEV1/FVC | 81.3 % | mir_device_print |  | Unknown | 63.945918878318 |  | 17.354081121683 | outside_rounding_tolerance |  | no_captured_value | No decoded cod_LAST field exists for this parameter. |

> وضعیت نرم‌افزاری فقط با نصف دقت خروجی رسمی سنجیده می‌شود؛ مقدار بازسازی‌شده دقت خروجی مستند و تأییدشده ندارد. تلورانس ستون قبل مربوط به مقدار خوانده‌شده دستگاه است.

**روش مقدار نرم‌افزاری:** `independent_positive_flow_integration_v1` — این مقدار بازسازی مستقل Parity Lab است، نه خروجی SpiroLab یا MIR.

## حدود اثبات و محدودیت‌ها

- Phase 1 is offline: the tool contains no USB transport and sends no command to any device.
- cod_LAST offsets and conversions are observed source-level claims, not MIR-certified protocol documentation.
- No device-level maneuver identifier is known. Association depends on the supplied capture token or an explicit operator attestation.
- A repeated or stale cod_LAST block can only be rejected when it is byte-identical; changed stale data cannot be detected from the block alone.
- cod_CURVE is preserved raw but has no decoder in the reviewed source. Software values in this tool are independent reconstructions, not SpiroLab or MIR output.
- The .mir archive format is treated as opaque and is never equated with raw USB capture data.
- Official MIR manuals are distributed through MyMIR login; no public Spirolab service manual or USB protocol specification was obtained.
- The Windows SDK is not openly downloadable: MIR's brochure requires a license agreement and a minimum device order.
- The reviewed PacketDecoder.cs uses raw - 65535 for the negative-flow branch, one unit away from conventional two's complement; the tool records this as unverified source evidence.
- No BTPS, calibration, leak, cough, acceptability or clinical-validity claim is established by a numerical difference.
- ATS/ERS between-maneuver repeatability is not used as an allowed error between two software products for the same maneuver.
- Synthetic fixtures test plumbing and arithmetic only; they are not MIR measurements and cannot establish parity.

## نتیجه‌گیری فاز ۱

این گزارش فقط شواهد ساختاری و اختلاف عددی قابل‌ردیابی را ثبت می‌کند. برای ادعای تطابق با MIR باید مانور واحد با شواهد غیرهویتی کافی، خروجی رسمی دستگاه/نرم‌افزار MIR، و در صورت امکان SDK رسمی مقایسه شود.
