# وضعیت مستندات و دسترسی رسمی MIR

آخرین بازبینی وب: **۲ نوامبر ۲۰۲۶**. محتوای سایت MIR ممکن است تغییر کند؛ لینک‌ها و نقل‌قول‌ها برای همین تاریخ است.

## ۱. خلاصه وضعیت

| پرسش | وضعیت فعلی | شاهد |
|---|---|---|
| آیا SDK رسمی Windows وجود دارد؟ | بله، اما عمومی و آزاد نیست | بروشور SDK |
| آیا SDK با Spirolab کار می‌کند؟ | بله، Spirolab با گزینه oximetry در فهرست سازگار است | بروشور SDK |
| آیا SDK پروتکل USB را مستند می‌کند؟ | SDK ارتباط را پیاده‌سازی می‌کند؛ پروتکل به‌صورت PDF جداگانه در اختیار توسعه‌دهنده قرار می‌گیرد | بروشور SDK |
| آیا SDK شامل تفسیر است؟ | بله: predicted values، interpretation، acceptability، repeatability، variability، quality grade | بروشور SDK |
| چگونه SDK گرفت؟ | تماس با نماینده MIR، امضای License Agreement، حداقل سفارش اسپیرومتر، دانلود از سرور MIR | بروشور SDK |
| آیا مشخصات عمومی `.mir` وجود دارد؟ | در این بازبینی مشخصات عمومی پیدا نشد | جست‌وجوی وب/صفحات MIR |
| آیا دفترچه کامل Spirolab عمومی است؟ | بروشور/دیتاشیت عمومی هست؛ راهنمای کامل/سرویس از MyMIR | صفحه Spirolab + MyMIR |
| آیا winspiroPRO جاری است؟ | خیر؛ MIR آن را legacy و بدون نگهداری فعال معرفی می‌کند | صفحه Winspiro PRO |
| آیا MIR Spiro نسل جاری است؟ | بله؛ سازگار با همه دستگاه‌های حرفه‌ای MIR | صفحه MIR Spiro |
| آیا MIR Spiro ورود داده از دستگاه دارد؟ | بله: «Import of tests from MIR professional devices» | صفحه MIR Spiro |
| آیا خروجی CSV تک‌مانور مستند است؟ | در صفحات عمومی تأیید نشد | صفحات MIR Spiro/Software |
| آیا SDK یا credential دریافت شد؟ | خیر | این پروژه |

## ۲. صفحه Software

منبع: [MIR — Desktop software, mobile apps & development tools](https://spirometry.com/en/software/)

نقل‌قول‌های کلیدی:

- MIR Spiro Platinum: «latest generation software for Spirometry and Oximetry compatible with all professional MIR devices».
- Winspiro PRO: «legacy Windows-based software solution» و «no longer actively maintained».
- SDK Windows: «Allow the USB and Bluetooth 2.0 Communication between your software and MIR spirometers. Include the algorithm for Automatic Interpretation, Quality Control Grade and a wide list of predicted sets and values.»
- جدول سازگاری SDK: `Spirolab; Spirodoc; Spirobank II Advanced` با oximetry؛ `Minispir; Spirobank II Basic` بدون oximetry.
- OS: Windows 7/8/10 با .NET Framework 4.6.1؛ زبان C#.
- ارتباط: USB و Bluetooth 2.0؛ برای Minispir و Spirobank II Basic فقط USB.

## ۳. بروشور SDK Windows

منبع: [Brochure SDK/API Windows (PDF)](https://spirometry.com/media/documents/Brochure_NoNDA_SDK_Windows.pdf)

نقل‌قول مستقیم:

> «To enable a “direct” communication between MIR devices and 3rd party software via USB, MIR is proud to offer the Software Development Kit for Windows-based applications. The SDK is a collection of libraries (.dll files for Microsoft .NET applications) that allows the Communication with MIR spirometers and also include the algorithm for the Interpretation of spirometry tests results.»

> «How to get the SDK: 1. get in touch with a MIR representative and introduce your projects and needs; 2. Sign the License Agreement and proceed with a minimum order of Spirometers; 3. Download the SDK from MIR server and start your development.»

> «Why choose the SDK instead of the USB communication Protocol? … COMMUNICATION PROTOCOL … a pdf file describing how to communicate with MIR devices … To be implemented by the Client Application.»

نتیجه:

- SDK یک بستهٔ .NET DLL است، نه یک مشخصات عمومی.
- پروتکل USB به‌صورت PDF جداگانه وجود دارد، اما از مسیر مجوز/نماینده MIR.
- ابزار Parity Lab در فاز ۱ هیچ SDK یا PDF پروتکل را دریافت نکرده است.
- بنابراین ادعای «پوشش کامل پارامترهای SDK» یا «پروتکل رسمی تأییدشده» در این فاز مجاز نیست.

## ۴. Spirolab

منبع: [MIR — Spirolab](https://spirometry.com/en/products/spirolab/)

نقل‌قول‌ها:

- «complete desktop spirometer with an oximetry option».
- «7'' color touchscreen and an embedded thermal printer».
- «operates both in Stand-Alone mode and connected to a PC via USB cable».
- «Storage up to 10.000 spirometric tests or 500 hours of oximetry».
- «Real-time testing … FVC, VC, MVV, PRE/POST bronchodilator comparison».
- «Predicted values … GLI, and ERS».
- «Automatic BTPS conversion».
- «Integration via MIR Spiro Software with EMR/EHR (in HL7, GDT, FHIR, EXCHANGE PROTOCOL)».
- بروشور عمومی: [Spirolab v11 EN](https://spirometry.com/media/documents/Spirolab_v11_EN_LQP.pdf)
- دیتاشیت: [MIR Spirolab datasheet](https://spirometry.com/media/documents/MIR-Linea-Standard-Spirolab-EN-datasheet.pdf)

این‌ها قابلیت‌های محصول‌اند؛ آفست بایت، فرمت `.mir` یا دستور خواندن حافظه را مشخص نمی‌کنند.

## ۵. MIR Spiro

منبع: [MIR — MIR Spiro Software](https://spirometry.com/en/software/mir-spiro/)

نقل‌قول‌ها:

- «latest generation software for Spirometry and Oximetry compatible with all professional MIR devices».
- «DATA IMPORT — Import of tests from MIR professional devices.»
- «ANONYMIZATION FUNCTIONALITY — Patient list, printing, data export.»
- «COMPREHENSIVE AND CUSTOMIZABLE PRINTS — ATS2019, Winspiro classic, NIOSH, OSHA.»
- «EMR/EHR INTEGRATION … HL7, FHIR (Json), GDT, DICOM, eXchange Protocol».

نتیجه:

- ورود داده از دستگاه و خروجی داده وجود دارد.
- اما در صفحات عمومی، قالب «CSV تک‌مانور با دقت مشخص» یا «API عمومی برای خواندن همان مانور» تأیید نشد.
- FHIR/HL7/GDT برای تبادل داده هستند، نه اثبات قالب خام یا تطابق عددی.

## ۶. MyMIR و دفترچه‌ها

- [MyMIR Login](https://mymir.spirometry.com/Login.aspx)
- صفحه Software به MyMIR و Dealer Area اشاره می‌کند.
- بروشور و دیتاشیت Spirolab عمومی‌اند.
- راهنمای کامل دستگاه/سرویس و برخی اسناد فنی از مسیر MyMIR با ورود حساب در دسترس‌اند.
- در این بازبینی، مشخصات عمومی USB یا فرمت `.mir` از صفحات عمومی MIR به‌دست نیامد.

## ۷. پاسخ به پرسش‌های پژوهشی

### ۷.۱ دستگاه چه پارامترهایی را از USB ارائه می‌دهد؟

**پاسخ فاز ۱:** فقط این‌ها با شاهد سورس قابل بیان‌اند:

- `FVC`, `FEV1`, `PEF`, `FET`, `PEF time`
- `quality/reserved` خام
- بسته‌های زنده `0x10`, `0x20`, `0x01`, `0x04`, `0xFF`
- `cod_CURVE` خام

**هنوز ناشناخته:** فهرست کامل پارامترهای SDK، معنا و واحد دقیق `0x20`, `0x01`، بایت‌های ناشناخته، پارامترهای ذخیره‌شده در حافظه که در `cod_LAST` نیستند.

### ۷.۲ آیا مقادیر SpiroLab با MIR برابرند؟

**پاسخ فاز ۱:** بدون داده واقعی جفت‌شده، **قابل پاسخ نیست**. نمونه مصنوعی فقط مسیر محاسبه و گزارش را آزمون می‌کند. هیچ ادعای برابری مجاز نیست.

### ۷.۳ آیا `.mir` همان داده خام USB است؟

**پاسخ:** خیر. `.mir` یک آرشیو اختصاصی است و در این ابزار opaque نگه داشته می‌شود. هیچ فرضی درباره ساختار آن نمی‌شود.

### ۷.۴ آیا می‌توان یک مانور را از حافظه دستگاه خواند؟

**پاسخ:** SDK برای «communication» و «read results» طراحی شده است، اما دستور دقیق خواندن رکورد حافظه در اسناد عمومی نیست. این کار نیازمند SDK رسمی یا پروتکل مجاز است. ابزار فاز ۱ چنین کاری نمی‌کند.

### ۷.۵ آیا می‌توان همان مانور را به نرم‌افزار رسمی MIR منتقل کرد؟

**پاسخ:** MIR Spiro «Import of tests from MIR professional devices» دارد؛ اما جریان دقیق برای یک مانور خاص، شناسه جفت‌سازی و قالب خروجی عمومی تأیید نشد. این مورد در فاز ۲ باید با نماینده MIR یا آزمون کنترل‌شده روشن شود.

### ۷.۶ آیا SDK قابل دریافت است؟

**پاسخ:** فقط از طریق نماینده MIR، قرارداد مجوز و حداقل سفارش دستگاه. دانلود عمومی/آزاد وجود ندارد.

### ۷.۷ آیا SDK با firmware فعلی Spirolab سازگار است؟

**پاسخ:** سازگاری مدل اعلام شده است، اما ماتریس firmware/نسخه SDK عمومی نیست. باید از MIR پرسیده شود.

### ۷.۸ آیا خروجی رسمی قابل استناد برای مقایسه داریم؟

**پاسخ:** برای فاز ۱ فقط قالب CSV/دستیِ خود ابزار. برای فاز ۲ باید خروجی نمایشگر، پرینت و نرم‌افزار رسمی همان مانور با شاهد غیرهویتی جمع‌آوری شود.

## ۸. چه چیزی باید از MIR درخواست شود؟

1. آخرین نسخه SDK Windows و فهرست پارامترهای آن.
2. PDF پروتکل USB و ماتریس سازگاری firmware.
3. راهنمای کاربر/سرویس Spirolab برای نسخه firmware موجود.
4. توضیح رسمی فرمت `.mir` و اینکه آیا معادل capture خام است یا نه.
5. روش رسمی جفت‌سازی یک مانور بین نمایشگر، پرینت، حافظه و نرم‌افزار.
6. قالب خروجی رسمی تک‌مانور و دقت گردکردن هر پارامتر.
7. مجوز/شرایط استفاده پژوهشی و محدودیت‌های انتشار.

## ۹. محدودیت اثبات

- هیچ SDK، PDF پروتکل، حساب MyMIR یا credential در این پروژه دریافت نشده است.
- هیچ آزمون سخت‌افزاری انجام نشده است.
- هیچ ادعای «پروتکل رسمی MIR» بر پایه سورس SpiroLab ساخته نمی‌شود.
- تمام نقل‌قول‌ها برای تاریخ بازبینی وب هستند و ممکن است منسوخ شوند.

## ۱۰. لینک‌های مرجع

- [MIR Software](https://spirometry.com/en/software/)
- [MIR Spiro](https://spirometry.com/en/software/mir-spiro/)
- [Winspiro PRO](https://spirometry.com/en/software/winspiropro/)
- [Spirolab](https://spirometry.com/en/products/spirolab/)
- [SDK Windows brochure](https://spirometry.com/media/documents/Brochure_NoNDA_SDK_Windows.pdf)
- [Spirolab brochure](https://spirometry.com/media/documents/Spirolab_v11_EN_LQP.pdf)
- [Spirolab datasheet](https://spirometry.com/media/documents/MIR-Linea-Standard-Spirolab-EN-datasheet.pdf)
- [MyMIR](https://mymir.spirometry.com/Login.aspx)
