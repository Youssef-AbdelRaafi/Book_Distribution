# دليل تشغيل نظام توزيع الكتب — تعليمات للعميل

## أولاً: متطلبات التشغيل
- ويندوز 10 أو 11
- **Docker Desktop** للويندوز (يتم تحميله من https://www.docker.com/products/docker-desktop/)
- 1 جيجابايت RAM فارغ على الأقل
- 5 جيجابايت مساحة تخزين فارغة

## ثانيًا: تحميل وتثبيت Docker Desktop
1. اذهب إلى https://www.docker.com/products/docker-desktop/
2. اضغط **Download for Windows**
3. شغّل الملف اللي نزلته (`Docker Desktop Installer.exe`)
4. اتبع التعليمات (الخيارات الافتراضية مناسبة)
5. **أعد تشغيل الكمبيوتر** بعد التثبيت
6. افتح Docker Desktop من قائمة ابدأ وانتظر علامة التشغيل (العلامة الخضراء في أسفل الشاشة)

## ثالثًا: تشغيل التطبيق
1. افتح **PowerShell** (من قائمة ابدأ اكتب PowerShell واضغط Enter)
2. اكتب الأوامر التالية بالترتيب:

```powershell
# انتقل إلى مجلد المشروع (غير المسار حسب مكان الملفات عندك)
cd C:\Book_Distribution

# شغّل التطبيق
docker compose up -d
```

3. انتظر 30 ثانية حتى يشتغل التطبيق بالكامل
4. افتح المتصفح (Google Chrome أو Edge) واكتب:
   ```
   http://localhost:8080
   ```

## رابعًا: تسجيل الدخول
- **Username**: admin
- **Password**: Admin@2026

## تنبيه مهم قبل التشغيل
قبل تشغيل `docker compose up -d`، تأكد من:
1. ملف `.env` موجود في نفس مجلد المشروع (جنب `docker-compose.yml`)
2. ملف `.env` يحتوي على `JWT_SIGNING_KEY` قوي (أقل حاجة 32 حرف)
3. ملف `.env` يحتوي على `ADMIN_PASSWORD_HASH` (اختياري — لو مش موجود، الباسورد هيكون `admin@123`)
4. Docker Desktop شغال (العلامة الخضراء)

## خامسًا: إيقاف التطبيق
- لو عاوز تطفى التطبيق: في PowerShell اكتب:
  ```powershell
  docker compose down
  ```
- لو عاوز تطفى وتحذف البيانات (مسح كل الفواتير والمكتبات): 
  ```powershell
  docker compose down -v
  ```
  **⚠️ تحذير:** الأمر اللي فوق يمسح كل البيانات ولا يمكن استرجاعها!

## سادسًا: النسخ الاحتياطي
التطبيق بيعمل نسخة احتياطية تلقائيًا كل يوم الساعة 2:00 AM.
النسخ موجودة في مجلد مخصص ولا تتأثر بإيقاف التطبيق.

لو عاوز تعمل نسخة يدويًا:
```powershell
docker exec book_distribution_app /app/backup-db.sh
```

## سابعًا: مشاكل وحلول

### المشكلة: "Port is already in use"
حل: غيّر البورت في ملف `docker-compose.yml` من `8080:8080` إلى `9090:8080` مثلاً، وافتح `http://localhost:9090` في المتصفح.

### المشكلة: التطبيق مش شغال بعد docker compose up
حل:
```powershell
docker compose logs
```
وأرسل اللي يظهر معانا عشان نساعدك.

### المشكلة: نسيت كلمة السر
- ارسل لينا نرسلك hash جديد ونحدثه في ملف `.env`
- أو امسح قاعدة البيانات بـ `docker compose down -v` واشتغل من أول وجديد (بيرجع admin / Admin@2026)

## ملاحظة مهمة
- البيانات محفوظة في **Docker volume** اسمه `book-data` — حتى لو حذفت التطبيق وركّبته تاني، البيانات لسة موجودة
- لو حذفت الـ volume (`docker compose down -v`)، كل البيانات تروح وتيجي من أول وجديد مع seeding
