using BookDistributionAPI.Features.AcademicYears;
using BookDistributionAPI.Features.Auth;
using BookDistributionAPI.Features.Governorates;
using BookDistributionAPI.Features.Semesters;
using BookDistributionAPI.Features.Settings;
using BookDistributionAPI.Features.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookDistributionAPI.Data;

public static class SeedData
{
    public static async Task InitializeAsync(
        AppDbContext db,
        ILogger logger,
        string bootstrapAdminPasswordHash,
        CancellationToken cancellationToken = default)
    {
        if (!PasswordHasher.IsSupportedHashFormat(bootstrapAdminPasswordHash))
        {
            throw new InvalidOperationException(
                "A valid Auth:BootstrapAdminPasswordHash is required for an empty database.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await db.AcademicYears.AnyAsync(cancellationToken))
            {
                var year2025 = new AcademicYear
                {
                    Name = "2025-2026",
                    IsActive = false
                };
                db.AcademicYears.Add(year2025);
                await db.SaveChangesAsync(cancellationToken);

                db.Semesters.AddRange(
                    new Semester { AcademicYearId = year2025.Id, Name = "الفصل الأول", Code = "A", IsActive = false },
                    new Semester { AcademicYearId = year2025.Id, Name = "الفصل الثاني", Code = "B", IsActive = false });

                var currentYear = DateTime.UtcNow.Year;
                var year2026 = new AcademicYear
                {
                    Name = $"{currentYear}-{currentYear + 1}",
                    IsActive = true
                };
                db.AcademicYears.Add(year2026);
                await db.SaveChangesAsync(cancellationToken);

                db.Semesters.AddRange(
                    new Semester { AcademicYearId = year2026.Id, Name = "الفصل الأول", Code = "A", IsActive = true },
                    new Semester { AcademicYearId = year2026.Id, Name = "الفصل الثاني", Code = "B", IsActive = false });

                var governorate = new Governorate { Name = "غير محدد" };
                db.Governorates.Add(governorate);
                await db.SaveChangesAsync(cancellationToken);
                db.Cities.Add(new City { Name = "غير محدد", GovernorateId = governorate.Id });
                await db.SaveChangesAsync(cancellationToken);
            }

            if (!await db.AppSettings.AnyAsync(cancellationToken))
            {
                db.AppSettings.AddRange(
                    new AppSetting { Key = "brandName", Value = string.Empty },
                    new AppSetting { Key = "phones", Value = string.Empty },
                    new AppSetting { Key = "mainCurrency", Value = "R.O." },
                    new AppSetting { Key = "subCurrency", Value = "Bz" },
                    new AppSetting { Key = "ownerSignatureName", Value = string.Empty },
                    new AppSetting { Key = "whatsappNumber", Value = string.Empty });
            }

            if (!await db.Users.AnyAsync(cancellationToken))
            {
                db.Users.Add(new User
                {
                    Username = "admin",
                    PasswordHash = bootstrapAdminPasswordHash,
                    Role = "Admin",
                    IsActive = true
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Seed failed: {Message}", ex.Message);
            throw;
        }
    }
}
