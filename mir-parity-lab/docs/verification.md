# تأیید ایزوله‌سازی شاخه `test`

این سند وضعیت Git و اثبات عدم دست‌کاری `main` و `mir` را ثبت می‌کند.

## ۱. مخزن و worktree

| مورد | مقدار |
|---|---|
| مخزن اصلی | `C:\Users\Ali\Desktop\spirolab2` |
| شاخه مخزن اصلی | `mir` |
| worktree ایزوله | `C:\Users\Ali\Desktop\spirolab2-test` |
| شاخه worktree | `test` |
| مبنای `test` | `4d02c8f401e1d6c5c8045debea23ced35a2ffbf3` |
| این مبنا برابر است با | `mir` و `origin/mir` |
| `origin/main` مبنا | `87188b766eddb4f0ee68fb0ebdd3783b7dde06fe` |
| tag موجود | `stable-mir-2026-11-02` = `cf5452372d0cecca00ac75f82a4578e12b0c6ac5` |

`git worktree list` در زمان اجرا:

```text
C:/Users/Ali/Desktop/spirolab2       4d02c8f [mir]
C:/Users/Ali/Desktop/spirolab2-test  4d02c8f [test]
```

## ۲. نحوه ساخت شاخه

- شاخه `test` از آخرین `mir` ساخته شد.
- اگر `test` از قبل وجود داشت، حذف/بازنویسی نشد؛ وجود آن بررسی و worktree جداگانه استفاده شد.
- هیچ merge، reset سخت، force-push یا تغییر تاریخچه انجام نشد.
- تمام تغییرات فاز ۱ فقط در `test` و فقط زیر `mir-parity-lab/` به‌علاوه یک استثنای `.gitignore` هستند.
- `main` و `mir` هیچ کامیت جدیدی دریافت نکردند.

## ۳. دستورهای تأیید

```bat
git -C C:\Users\Ali\Desktop\spirolab2-test branch --show-current
git -C C:\Users\Ali\Desktop\spirolab2-test log --oneline --decorate -5
git -C C:\Users\Ali\Desktop\spirolab2-test merge-base test mir
git -C C:\Users\Ali\Desktop\spirolab2-test rev-list --count mir..test
git -C C:\Users\Ali\Desktop\spirolab2-test rev-parse test mir origin/main origin/mir
git -C C:\Users\Ali\Desktop\spirolab2 status --short
```

## ۴. نتیجه قبل از commit

```text
branch: test
HEAD:   4d02c8f (base, no phase-1 commit yet)
mir:    4d02c8f401e1d6c5c8045debea23ced35a2ffbf3
origin/main: 87188b766eddb4f0ee68fb0ebdd3783b7dde06fe
origin/mir:  4d02c8f401e1d6c5c8045debea23ced35a2ffbf3
working tree root (mir): clean
```

## ۵. کامیت فاز ۱

| مورد | مقدار |
|---|---|
| کامیت کد/مستندات | `985be027e3ea32edc24f0f00c41f2c1a90957efc` |
| کامیت ثبت تأیید | HEAD همین فایل (کامیت دوم `test`) |
| فایل‌های تغییریافته | فقط `mir-parity-lab/**` و `.gitignore` |
| merge commit | ندارد |

## ۶. نتیجه بعد از commit

```text
HEAD:        HEAD of `test` (this verification commit; run `git rev-parse test`)
mir:         4d02c8f401e1d6c5c8045debea23ced35a2ffbf3
origin/mir:  4d02c8f401e1d6c5c8045debea23ced35a2ffbf3
origin/main: 87188b766eddb4f0ee68fb0ebdd3783b7dde06fe
```

- `test` دقیقاً یک کامیت روی مبنا دارد (قبل از کامیت تأیید).
- هیچ merge commit وجود ندارد.
- `main` و `mir` هیچ تغییر محلی/دور جدیدی ندارند.

## ۷. push

- فقط `test` push می‌شود.
- هرگز `main` یا `mir` push نمی‌شود.
- هیچ force push انجام نمی‌شود.
- مجوز صریح مالک در همین گفتگو برای push شاخه `test` ثبت شده است.

## ۸. هشدار

وجود یک شاخه `test` و کامیت، به‌تنهایی اثبات برابری با MIR نیست. این سند فقط ایزوله‌سازی Git و قابلیت بازتولید را تأیید می‌کند.
