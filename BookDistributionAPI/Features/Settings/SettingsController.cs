using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;


namespace BookDistributionAPI.Features.Settings;

[ApiController]
[Authorize]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    public SettingsController(AppDbContext db) => _db = db;

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        // Unauthenticated users get only branding — no phone/whatsapp data
        var keys = new[] { "brandName", "mainCurrency", "subCurrency" };
        var settings = await _db.AppSettings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, cancellationToken);

        var dto = new
        {
            BrandName = settings.GetValueOrDefault("brandName")?.Value ?? "",
            MainCurrency = settings.GetValueOrDefault("mainCurrency")?.Value ?? "R.O.",
            SubCurrency = settings.GetValueOrDefault("subCurrency")?.Value ?? "Bz"
        };
        return Ok(ApiResponse<object>.Ok(dto));
    }

    [HttpGet("full")]
    [Authorize]
    public async Task<IActionResult> GetFull(CancellationToken cancellationToken)
    {
        var keys = new[] { "brandName", "phones", "mainCurrency", "subCurrency", "ownerSignatureName", "whatsappNumber" };
        var settings = await _db.AppSettings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, cancellationToken);

        var dto = new SettingsDto
        {
            BrandName = settings.GetValueOrDefault("brandName")?.Value ?? "",
            Phones = settings.GetValueOrDefault("phones")?.Value ?? "",
            MainCurrency = settings.GetValueOrDefault("mainCurrency")?.Value ?? "R.O.",
            SubCurrency = settings.GetValueOrDefault("subCurrency")?.Value ?? "Bz",
            OwnerSignatureName = settings.GetValueOrDefault("ownerSignatureName")?.Value ?? "مدحت محمد عبد الستار",
            WhatsAppNumber = settings.GetValueOrDefault("whatsappNumber")?.Value ?? "91913020"
        };
        return Ok(ApiResponse<object>.Ok(dto));
    }

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update([FromBody] SettingsDto dto, CancellationToken cancellationToken)
    {
        var settings = new[]
        {
            new { Key = "brandName", Value = dto.BrandName },
            new { Key = "phones", Value = dto.Phones },
            new { Key = "mainCurrency", Value = dto.MainCurrency },
            new { Key = "subCurrency", Value = dto.SubCurrency },
            new { Key = "ownerSignatureName", Value = dto.OwnerSignatureName ?? "" },
            new { Key = "whatsappNumber", Value = dto.WhatsAppNumber ?? "" }
        };

        var existingSettings = await _db.AppSettings
            .Where(s => settings.Select(x => x.Key).Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, cancellationToken);

        foreach (var setting in settings)
        {
            if (existingSettings.TryGetValue(setting.Key, out var existing))
                existing.Value = setting.Value;
            else
                _db.AppSettings.Add(new AppSetting { Key = setting.Key, Value = setting.Value });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(true, "تم تحديث الإعدادات"));
    }

}
