using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Features.AcademicYears;
using BookDistributionAPI.Features.Semesters;
using BookDistributionAPI.Features.Governorates;
using BookDistributionAPI.Features.Books;
using BookDistributionAPI.Features.Settings;
using BookDistributionAPI.Features.Libraries;
using BookDistributionAPI.Features.Users;
using BookDistributionAPI.Features.Auth;

namespace BookDistributionAPI.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await db.AcademicYears.AnyAsync())
            {
                var currentYear = DateTime.Now.Year;
                var nextYear = currentYear + 1;
                var year = new AcademicYear { Name = $"{currentYear}-{nextYear}", IsActive = true };
                db.AcademicYears.Add(year);
                await db.SaveChangesAsync();

                var sem1 = new Semester { AcademicYearId = year.Id, Name = "الفصل الأول", Code = "A", IsActive = true };
                var sem2 = new Semester { AcademicYearId = year.Id, Name = "الفصل الثاني", Code = "B", IsActive = false };
                db.Semesters.AddRange(sem1, sem2);
                await db.SaveChangesAsync();

                var govs = new Dictionary<string, string[]>
                {
                    ["محافظة مسقط"] = new[] { "مسقط", "بوشر", "السيب", "مطرح", "العامرات", "قريات" },
                    ["محافظة ظفار"] = new[] { "صلالة", "طاقة", "مرباط", "ثمريت", "رخيوت", "ضلكوت", "المزيونة", "سدح", "شليم وجزر الحلانيات" },
                    ["محافظة مسندم"] = new[] { "خصب", "بخا", "دبا", "مدحا" },
                    ["محافظة البريمي"] = new[] { "البريمي", "محضة", "السنينة" },
                    ["محافظة الداخلية"] = new[] { "نزوى", "بهلاء", "سمائل", "إزكي", "الحمراء", "أدم", "منح", "بدبد" },
                    ["محافظة شمال الباطنة"] = new[] { "صحار", "شناص", "لوى", "صحم", "الخابورة", "السويق" },
                    ["محافظة جنوب الباطنة"] = new[] { "الرستاق", "بركاء", "المصنعة", "نخل", "العوابي", "وادي المعاول" },
                    ["محافظة جنوب الشرقية"] = new[] { "صور", "جعلان بني بوعلي", "جعلان بني بوحسن", "الكامل والوافي", "مصيرة" },
                    ["محافظة شمال الشرقية"] = new[] { "إبراء", "المضيبي", "بدية", "القابل", "وادي بني خالد", "دماء والطائيين" },
                    ["محافظة الظاهرة"] = new[] { "عبري", "ينقل", "ضنك" },
                    ["محافظة الوسطى"] = new[] { "هيما", "محوت", "الدقم", "الجازر" }
                };

                var governorates = new List<Governorate>();
                var cities = new List<City>();

                foreach (var gov in govs)
                {
                    var governorate = new Governorate { Name = gov.Key };
                    governorates.Add(governorate);
                }

                db.Governorates.AddRange(governorates);
                await db.SaveChangesAsync();

                foreach (var gov in govs)
                {
                    var governorate = governorates.First(g => g.Name == gov.Key);
                    foreach (var cityName in gov.Value)
                    {
                        cities.Add(new City { Name = cityName, GovernorateId = governorate.Id });
                    }
                }

                db.Cities.AddRange(cities);
                await db.SaveChangesAsync();
            } // End of if !db.AcademicYears.Any()

            var activeYear = await db.AcademicYears.FirstOrDefaultAsync(y => y.IsActive);
            if (activeYear == null)
            {
                await transaction.CommitAsync();
                return;
            }

            var bookTemplates = new[]
            {
                // Semester 1 Books
                new { Name = "فيزياء الصف التاسع (كتاب واحد)", Grade = "إصدارات الصف التاسع", Subject = "فيزياء", Semester = "الفصل الأول", Price = 3.000m },
                new { Name = "كيمياء الصف التاسع (كتابين)", Grade = "إصدارات الصف التاسع", Subject = "كيمياء", Semester = "الفصل الأول", Price = 3.000m },
                new { Name = "فيزياء الصف العاشر (كتاب واحد)", Grade = "إصدارات الصف العاشر", Subject = "فيزياء", Semester = "الفصل الأول", Price = 3.000m },
                new { Name = "كيمياء الصف العاشر (كتابين)", Grade = "إصدارات الصف العاشر", Subject = "كيمياء", Semester = "الفصل الأول", Price = 3.000m },
                new { Name = "فيزياء الحادي عشر (كتابين)", Grade = "إصدارات الصف الحادي عشر", Subject = "فيزياء", Semester = "الفصل الأول", Price = 3.500m },
                new { Name = "العلوم البيئية (القسم الأدبي)", Grade = "إصدارات الصف الحادي عشر", Subject = "علوم بيئية", Semester = "الفصل الأول", Price = 3.500m },
                new { Name = "فيزياء الثاني عشر (كتاب واحد)", Grade = "إصدارات الصف الثاني عشر", Subject = "فيزياء", Semester = "الفصل الأول", Price = 4.000m },
                new { Name = "العلوم البيئية ثاني عشر (كتاب واحد)", Grade = "إصدارات الصف الثاني عشر", Subject = "علوم بيئية", Semester = "الفصل الأول", Price = 3.500m },
                new { Name = "كيمياء الثاني عشر (كتاب واحد)", Grade = "إصدارات الصف الثاني عشر", Subject = "كيمياء", Semester = "الفصل الأول", Price = 4.000m },

                // Semester 2 Books
                new { Name = "فيزياء الصف التاسع (كتاب واحد)", Grade = "إصدارات الصف التاسع", Subject = "فيزياء", Semester = "الفصل الثاني", Price = 3.000m },
                new { Name = "كيمياء الصف التاسع (كتاب واحد)", Grade = "إصدارات الصف التاسع", Subject = "كيمياء", Semester = "الفصل الثاني", Price = 3.000m },
                new { Name = "فيزياء الصف العاشر (كتاب واحد)", Grade = "إصدارات الصف العاشر", Subject = "فيزياء", Semester = "الفصل الثاني", Price = 3.000m },
                new { Name = "فيزياء الحادي عشر (كتاب واحد)", Grade = "إصدارات الصف الحادي عشر", Subject = "فيزياء", Semester = "الفصل الثاني", Price = 4.000m },
                new { Name = "العلوم البيئية أدبي (كتاب واحد)", Grade = "إصدارات الصف الحادي عشر", Subject = "علوم بيئية", Semester = "الفصل الثاني", Price = 3.500m },
                new { Name = "فيزياء الثاني عشر (كتابين)", Grade = "إصدارات الصف الثاني عشر", Subject = "فيزياء", Semester = "الفصل الثاني", Price = 4.000m },
                new { Name = "العلوم البيئية أدبي (كتاب واحد)", Grade = "إصدارات الصف الثاني عشر", Subject = "علوم بيئية", Semester = "الفصل الثاني", Price = 3.500m }
            };

            var existingBooks = await db.Books.IgnoreQueryFilters().ToListAsync(cancellationToken);
            var booksToAdd = new List<Book>();

            var activeSem1 = await db.Semesters.FirstOrDefaultAsync(s => s.Name == "الفصل الأول" && s.AcademicYearId == activeYear.Id);
            var activeSem2 = await db.Semesters.FirstOrDefaultAsync(s => s.Name == "الفصل الثاني" && s.AcademicYearId == activeYear.Id);

            if (activeSem1 == null || activeSem2 == null)
            {
                await transaction.CommitAsync();
                return;
            }

            foreach (var template in bookTemplates)
            {
                var semesterId = template.Semester == "الفصل الأول" ? activeSem1.Id : activeSem2.Id;
                var exists = existingBooks.Any(eb => eb.SemesterId == semesterId && eb.Name == template.Name && eb.Grade == template.Grade);

                if (!exists)
                {
                    booksToAdd.Add(new Book
                    {
                        Name = template.Name,
                        Grade = template.Grade,
                        Subject = template.Subject,
                        SemesterId = semesterId,
                        Price = template.Price,
                        StockQuantity = 500
                    });
                }
            }

            db.Books.AddRange(booksToAdd);

            // Optional: update physics 9 and 10 in activeSem1 if their names were previously "كتابين"
            var phys9Sem1 = existingBooks.FirstOrDefault(eb => eb.SemesterId == activeSem1.Id && eb.Grade == "إصدارات الصف التاسع" && eb.Subject == "فيزياء" && eb.Name.Contains("كتابين"));
            if (phys9Sem1 != null) { phys9Sem1.Name = "فيزياء الصف التاسع (كتاب واحد)"; }

            var phys10Sem1 = existingBooks.FirstOrDefault(eb => eb.SemesterId == activeSem1.Id && eb.Grade == "إصدارات الصف العاشر" && eb.Subject == "فيزياء" && eb.Name.Contains("كتابين"));
            if (phys10Sem1 != null) { phys10Sem1.Name = "فيزياء الصف العاشر (كتاب واحد)"; }

            await db.SaveChangesAsync();

            if (!await db.AppSettings.AnyAsync())
            {
                var settings = new[]
                {
                    new AppSetting { Key = "brandName", Value = "سلسلة تدريبات كامبريدج في الفيزياء" },
                    new AppSetting { Key = "phones", Value = "إدارة المبيعات: هاتف: 91913020 - 98877925" },
                    new AppSetting { Key = "mainCurrency", Value = "R.O." },
                    new AppSetting { Key = "subCurrency", Value = "Bz" },
                    new AppSetting { Key = "ownerSignatureName", Value = "مدحت محمد عبد الستار" },
                    new AppSetting { Key = "whatsappNumber", Value = "91913020" },
                };
                db.AppSettings.AddRange(settings);
                await db.SaveChangesAsync();
            }

            var existingUsers = await db.Users.IgnoreQueryFilters().ToListAsync(cancellationToken);
            if (!existingUsers.Any(u => u.Username == "admin"))
            {
                var configuredAdminHash = Environment.GetEnvironmentVariable("ADMIN_PASSWORD_HASH");
                var adminHash = PasswordHasher.IsSupportedHashFormat(configuredAdminHash ?? string.Empty)
                    ? configuredAdminHash!
                    : PasswordHasher.Hash("admin@123");

                db.Users.Add(new User
                {
                    Username = "admin",
                    PasswordHash = adminHash,
                    Role = "Admin",
                    TenantId = 1,
                    IsActive = true
                });
            }

            if (!existingUsers.Any(u => u.Username == "guest"))
            {
                db.Users.Add(new User
                {
                    Username = "guest",
                    PasswordHash = PasswordHasher.Hash("guest99999"),
                    Role = "Guest",
                    TenantId = 2,
                    IsActive = true
                });
            }

            await db.SaveChangesAsync(cancellationToken);

            if (!await db.Libraries.AnyAsync())
            {
                var libData = new List<(string Gov, string City, string[] Libs)>
        {
            ("محافظة مسقط", "بوشر", new[] { "مكتبة الجامعة", "مكتبة الادريسي", "مكتبة السطر", "مكتبة العذيبة الادريسي" }),
            ("محافظة مسقط", "السيب", new[] { "مكتبة القلم الأخضر", "مكتبة الارتقاء", "مكتبة الجامعة (المعبيلة)", "مكتبة (مكتبتك)", "مكتبة مزون", "مكتبة الوردي", "مكتبة الجامعة للقراء", "مكتبة مسقط" }),
            ("محافظة مسقط", "العامرات", new[] { "مكتبة ورقة وقلم", "مكتبة دار المناهل" }),
            ("محافظة مسقط", "مطرح", new[] { "مكتبة الهداية" }),
            ("محافظة مسقط", "قريات", new[] { "مكتبة زهرة المدائن", "مكتبة المجد", "مكتبة قريات الثقافية" }),

            ("محافظة شمال الشرقية", "المضيبي", new[] { "مكتبة روائع نور الاستقامة", "مكتبة قرطاسية انهار سناو", "مكتبة المجرة المضيئة", "مكتبة كنوز العلم" }),
            ("محافظة شمال الشرقية", "إبراء", new[] { "مكتبة خلفان", "مكتبة دار العروبة" }),

            ("محافظة جنوب الشرقية", "صور", new[] { "مكتبة أطلس", "مكتبة طيور الجنة", "مكتبة الكأس" }),
            ("محافظة جنوب الشرقية", "الكامل والوافي", new[] { "مكتبة الكوفة" }),
            ("محافظة جنوب الشرقية", "جعلان بني بوعلي", new[] { "مكتبة الوافي" }),
            ("محافظة جنوب الشرقية", "جعلان بني بوحسن", new[] { "مكتبة نور الاستقامة", "مكتبة سما الإبداع" }),

            ("محافظة جنوب الباطنة", "الرستاق", new[] { "مكتبة دار النور", "مكتبة طبشورة" }),
            ("محافظة جنوب الباطنة", "بركاء", new[] { "مكتبة الفلاح", "مكتبة الطيف", "مكتبة المعراج", "مكتبة الراقي العالمية بالسوادي" }),
            ("محافظة جنوب الباطنة", "العوابي", new[] { "مكتبة الأمنيات الكبيرة" }),
            ("محافظة جنوب الباطنة", "المصنعة", new[] { "مكتبة الراقي" }),

            ("محافظة شمال الباطنة", "السويق", new[] { "مكتبة المتنبي", "مكتبة الفجر الجديد بالثرمد", "مكتبة الاتحاد بجوار ألوان", "مكتبة اليُسر (الخضراء سابقاً)", "مكتبة الفجر الجديد فرع الإسكان", "مكتبة زهي السلام", "مكتبة الوطن" }),
            ("محافظة شمال الباطنة", "الخابورة", new[] { "مكتبة دار العلم", "مكتبة شعاع القلم" }),
            ("محافظة شمال الباطنة", "صحم", new[] { "مكتبة دار الشروق", "مكتبة اللبيب", "مكتبة منار السبيل", "متجر العهود صحم" }),
            ("محافظة شمال الباطنة", "لوى", new[] { "مكتبة الكندي", "مكتبة مناهل العلم" }),
            ("محافظة شمال الباطنة", "صحار", new[] { "مكتبة الشرق الأوسط", "مكتبة برج القاهرة", "مكتبة روائع البيان", "مكتبة المدينة", "مكتبة روائع الأمل" }),
            ("محافظة شمال الباطنة", "شناص", new[] { "مكتبة الصدف" }),

            ("محافظة الظاهرة", "عبري", new[] { "مكتبة كنوز المعرفة", "مكتبة اقرأ" }),
            ("محافظة الظاهرة", "ينقل", new[] { "مكتبة واحة الظاهر" }),

            ("محافظة ظفار", "صلالة", new[] { "مكتبة الثقافة الاسلامية", "مكتبة الخريف فرع صلالة الجديدة", "مكتبة الخريف فرع شارع السلام", "مكتبة الخريف فرع السعادة", "مكتبة الهداية فرع صلالة الجديدة", "مكتبة الهداية فرع شارع السلام", "مكتبة A4 بصلحنوت", "مكتبة الصباح بعوقد" }),

            ("محافظة الداخلية", "نزوى", new[] { "مكتبة الابتكار", "مكتبة دبوس", "مكتبة الشهامة" }),
            ("محافظة الداخلية", "سمائل", new[] { "مكتبة بيت الجبل" }),
            ("محافظة الداخلية", "إزكي", new[] { "مكتبة الفضل بن الحواري", "مكتبة أجيال ازكي" }),
            ("محافظة الداخلية", "أدم", new[] { "مكتبة السيدة فاطمة الزهراء أدم" }),
            ("محافظة الداخلية", "بهلاء", new[] { "مكتبة واحة التفوق", "مكتبة الإثراء", "مكتبة الغبيراء", "مكتبة ابن الهاشمي", "مكتبة صدى القمة" }),
            ("محافظة الداخلية", "منح", new[] { "مكتبة غاية التميز" }),
            ("محافظة الداخلية", "الحمراء", new[] { "مكتبة الحمراء الحديثة" }),

            ("محافظة البريمي", "البريمي", new[] { "مكتبة السندباد", "متجر السعادة" }),
            ("محافظة الوسطى", "محوت", new[] { "متجر أشرف رشاد" })
        };

                var governoratesDict = await db.Governorates.ToDictionaryAsync(g => g.Name);
                var citiesDict = await db.Cities.ToDictionaryAsync(c => new { c.Name, c.GovernorateId });

                var librariesToAdd = new List<Library>();
                foreach (var mapping in libData)
                {
                    if (!governoratesDict.TryGetValue(mapping.Gov, out var gov))
                        continue;
                    
                    var cityKey = new { Name = mapping.City, GovernorateId = gov.Id };
                    if (!citiesDict.TryGetValue(cityKey, out var city))
                        continue;

                    foreach (var libName in mapping.Libs)
                    {
                        librariesToAdd.Add(new Library
                        {
                            Name = libName,
                            GovernorateId = gov.Id,
                            CityId = city.Id,
                            IsActive = true,
                            OwnerName = "",
                            OwnerPhone = "",
                            ResponsibleName = "",
                            ResponsiblePhone = "",
                            Shift1Start = "08:00",
                            Shift1End = "13:00",
                            Shift2Start = "16:00",
                            Shift2End = "22:00"
                        });
                    }
                }
                db.Libraries.AddRange(librariesToAdd);
                await db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Seed failed: {Message}", ex.Message);
            throw;
        }
    }

    public static async Task SeedGuestDemoDataAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var activeYear = await db.AcademicYears.FirstOrDefaultAsync(y => y.IsActive, cancellationToken);
        if (activeYear == null) return;

        var activeSem1 = await db.Semesters.FirstOrDefaultAsync(s => s.Name == "الفصل الأول" && s.AcademicYearId == activeYear.Id, cancellationToken);
        if (activeSem1 == null) return;

        const int guestTenantId = 2;

        if (!await db.AppSettings.IgnoreQueryFilters().AnyAsync(s => s.TenantId == guestTenantId, cancellationToken))
        {
            db.AppSettings.AddRange(new[]
            {
                new AppSetting { Key = "brandName", Value = "سلسلة تدريبات كامبريدج - حساب الزوار (تجريبي)", TenantId = guestTenantId },
                new AppSetting { Key = "phones", Value = "هاتف الزوار التجريبي: 90000000", TenantId = guestTenantId },
                new AppSetting { Key = "mainCurrency", Value = "R.O.", TenantId = guestTenantId },
                new AppSetting { Key = "subCurrency", Value = "Bz", TenantId = guestTenantId },
                new AppSetting { Key = "ownerSignatureName", Value = "مستخدم زائر (تجريبي)", TenantId = guestTenantId },
                new AppSetting { Key = "whatsappNumber", Value = "90000000", TenantId = guestTenantId }
            });
        }

        if (!await db.Books.IgnoreQueryFilters().AnyAsync(b => b.TenantId == guestTenantId, cancellationToken))
        {
            db.Books.AddRange(
                new Book { Name = "فيزياء الصف التاسع (تجريبي)", Grade = "إصدارات الصف التاسع", Subject = "فيزياء", SemesterId = activeSem1.Id, Price = 3.000m, StockQuantity = 100, TenantId = guestTenantId },
                new Book { Name = "كيمياء الصف العاشر (تجريبي)", Grade = "إصدارات الصف العاشر", Subject = "كيمياء", SemesterId = activeSem1.Id, Price = 3.500m, StockQuantity = 100, TenantId = guestTenantId }
            );
        }

        if (!await db.Libraries.IgnoreQueryFilters().AnyAsync(l => l.TenantId == guestTenantId, cancellationToken))
        {
            var gov = await db.Governorates.FirstOrDefaultAsync(cancellationToken);
            var city = await db.Cities.FirstOrDefaultAsync(cancellationToken);
            if (gov != null && city != null)
            {
                db.Libraries.Add(new Library
                {
                    Name = "مكتبة الأمل التجريبية (زائر)",
                    GovernorateId = gov.Id,
                    CityId = city.Id,
                    IsActive = true,
                    OwnerName = "أحمد التجريبي",
                    OwnerPhone = "98765432",
                    TenantId = guestTenantId
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
