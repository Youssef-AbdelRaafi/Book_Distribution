# دليل نشر الباك اند للإنتاج

## المتطلبات الأساسية

- .NET 10.0 SDK
- SQL Server 2019 أو أحدث
- خادم ويب (IIS أو Nginx أو Kestrel خلف reverse proxy)

## خطوات النشر

### 1. إعداد قاعدة البيانات

```bash
# إنشاء قاعدة بيانات جديدة في SQL Server
# استخدم SQL Server Authentication (ليس Windows Authentication)
```

### 2. إعداد Environment Variables

في الإنتاج، استخدم Environment Variables بدلاً من وضع الأسرار في ملفات التكوين:

**Windows (PowerShell):**
```powershell
$env:ConnectionStrings__DefaultConnection = "Server=YOUR_SERVER;Database=BookDistributionDB;User Id=YOUR_USER;Password=YOUR_PASSWORD;MultipleActiveResultSets=true;TrustServerCertificate=False;"
$env:Auth__AdminUsername = "admin"
$env:Auth__AdminPasswordHash = "YOUR_HASHED_PASSWORD"
$env:Auth__JwtSigningKey = "YOUR_MINIMUM_32_CHAR_RANDOM_KEY"
$env:Cors__AllowedOrigins__0 = "https://yourdomain.com"
```

**Linux:**
```bash
export ConnectionStrings__DefaultConnection="Server=YOUR_SERVER;Database=BookDistributionDB;User Id=YOUR_USER;Password=YOUR_PASSWORD;MultipleActiveResultSets=true;TrustServerCertificate=False;"
export Auth__AdminUsername="admin"
export Auth__AdminPasswordHash="YOUR_HASHED_PASSWORD"
export Auth__JwtSigningKey="YOUR_MINIMUM_32_CHAR_RANDOM_KEY"
export Cors__AllowedOrigins__0="https://yourdomain.com"
```

### 3. إنشاء كلمة مرور مشفرة

استخدم الأداة الموجودة في المشروع لتشفير كلمة المرور:

```bash
cd BookDistributionAPI/GenerateHash
dotnet run
```

أدخل كلمة المرور المرغوبة وانسخ الـ hash الناتج.

### 4. نشر التطبيق

```bash
cd BookDistributionAPI
dotnet publish -c Release -o ./publish
```

### 5. تشغيل التطبيق

```bash
cd publish
dotnet BookDistributionAPI.dll --environment Production
```

### 6. إعداد Reverse Proxy (موصى به)

**Nginx:**
```nginx
server {
    listen 80;
    server_name yourdomain.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

**IIS:**
استخدم ASP.NET Core Hosting Bundle وقم بإنشاء موقع جديد يشير إلى مجلد `publish`.

## التحقق من النشر

1. تأكد من عمل Health Check:
   ```bash
   curl https://yourdomain.com/health
   ```

2. تأكد من عمل Swagger (في Development فقط):
   ```
   https://yourdomain.com/swagger
   ```

3. تأكد من عمل الـ API:
   ```bash
   curl -X POST https://yourdomain.com/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"username":"admin","password":"yourpassword"}'
   ```

## الصيانة

### مراقبة السجلات

السجلات تُحفظ في مجلد `Logs/` بجانب التطبيق:
- `bookdistribution-YYYYMMDD.log`
- يتم الاحتفاظ بآخر 30 يوم

### النسخ الاحتياطي

- قم بعمل نسخ احتياطي لقاعدة البيانات يومياً
- قم بعمل نسخ احتياطي لمجلد `Logs/` أسبوعياً

### التحديثات

```bash
# إيقاف التطبيق
# نشر الإصدار الجديد
dotnet publish -c Release -o ./publish
# إعادة تشغيل التطبيق
```

## الأمان

- **لا تقم أبداً** بوضع connection string أو JWT signing key في الكود أو في git
- استخدم كلمات مرور قوية (على الأقل 16 حرف)
- استخدم HTTPS في الإنتاج
- قم بتحديث المكتبات بانتظام
- قم بمراقبة السجلات بحثاً عن نشاط مشبوه

## استكشاف الأخطاء

### التطبيق لا يعمل

1. تحقق من connection string
2. تحقق من Environment Variables
3. راجع السجلات في `Logs/`
4. تأكد من أن SQL Server يعمل

### أخطاء في قاعدة البيانات

1. تحقق من صلاحيات المستخدم
2. تأكد من أن قاعدة البيانات موجودة
3. تحقق من أن Migrations تم تطبيقها

### مشاكل في CORS

1. تأكد من أن `Cors:AllowedOrigins` يحتوي على النطاق الصحيح
2. تأكد من استخدام HTTPS في الإنتاج
