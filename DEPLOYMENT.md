# دليل تسليم وتشغيل نظام توزيع الكتب

## قبل التسليم

1. تأكد من وجود Docker Desktop وتشغيله على جهاز العميل.
2. احتفظ بنسخة خارجية من قاعدة البيانات وملفات النسخ الاحتياطي قبل نقل المشروع.
3. لا تُسلّم ملف `.env` بمفتاح أو كلمة مرور معروفة أو مستخدمة في مشروع آخر.
4. نفّذ الاختبارات الواردة في قسم التحقق أدناه.

## إعداد الأسرار لأول مرة

من مجلد المشروع:

```powershell
Copy-Item .env.example .env
```

أنشئ قيمة `JWT_SIGNING_KEY` فريدة وضعها في `.env`:

```powershell
$bytes = New-Object byte[] 48
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
$rng.Dispose()
[Convert]::ToBase64String($bytes)
```

أنشئ hash لكلمة مرور المدير ثم الصق الناتج في `ADMIN_PASSWORD_HASH` داخل `.env`:

```powershell
.\BookDistributionAPI\scripts\generate-admin-password-hash.ps1
```

الناتج يحتوي `$$` عمدًا؛ اتركه كما هو عند لصقه في ملف `.env`.

## التشغيل

```powershell
docker compose up -d --build
docker compose ps
```

افتح `http://localhost:8080` وسجل الدخول باسم `admin` وكلمة المرور التي اخترتها عند إنشاء الـhash.

## البيانات والنسخ الاحتياطي

- عند أول تشغيل فقط، تُنسخ `new_database.db` والشعارات المرفقة إلى Docker volume الدائم `book-data`.
- لن يكتب النظام فوق volume موجود؛ لذلك تُحافظ إعادة التشغيل أو التحديث على بيانات العميل.
- النسخة اليومية تُحفظ في `book-backups` الساعة 2:00 AM لمدة 30 يومًا.
- أنشئ نسخة يدوية قبل أي تحديث:

  ```powershell
  docker exec book_distribution_app /app/backup-db.sh
  ```

- لا تستخدم `docker compose down -v` إلا إذا كانت لديك نسخة احتياطية مؤكدة؛ فهذا الأمر يحذف البيانات والنسخ الاحتياطية نهائيًا.

## التحقق قبل الاستلام

```powershell
docker compose ps
Invoke-WebRequest http://localhost:8080/api/health
docker exec book_distribution_app /app/backup-db.sh
docker exec book_distribution_app ls -la /app/backups
```

ثم اختبر من الواجهة: تسجيل الدخول، صرف كتاب، مرتجع جزئي، سند قبض، عرض الرصيد، طباعة مستند، وإعادة تشغيل الحاوية. يجب أن تبقى البيانات موجودة بعد إعادة التشغيل.

## إيقاف النظام

```powershell
docker compose down
```

## RunAsp / IIS publishing

The tracked `cambridge.pubxml` is a password-free template. Create a local,
ignored profile before publishing and keep the MSDeploy password only there:

```powershell
Copy-Item .\BookDistributionAPI\Properties\PublishProfiles\cambridge.pubxml `
  .\BookDistributionAPI\Properties\PublishProfiles\cambridge.local.pubxml
```

Add the MSDeploy `<Password>` element only to `cambridge.local.pubxml`, then
publish with the helper below. It temporarily injects the JWT signing key from
the ignored `.env` file into the deployment package and removes that temporary
file immediately after the publish completes.

```powershell
.\BookDistributionAPI\scripts\publish-runasp.ps1 -PublishProfile cambridge.local
```

For HTTPS, first enable a valid TLS certificate and the port-443 binding for
`cambridge.runasp.net` in the RunAsp control panel. Verify
`https://cambridge.runasp.net/api/health` succeeds before enabling an HTTP to
HTTPS redirect. Never commit deployment passwords, JWT keys, or production
settings files.
