using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;

namespace BookDistributionAPI.Features.Settings;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    public SettingsController(AppDbContext db) => _db = db;

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get()
    {
        var settings = await _db.AppSettings.ToListAsync();
        var dto = new SettingsDto
        {
            BrandName = settings.FirstOrDefault(s => s.Key == "brandName")?.Value ?? "",
            Phones = settings.FirstOrDefault(s => s.Key == "phones")?.Value ?? "",
            MainCurrency = settings.FirstOrDefault(s => s.Key == "mainCurrency")?.Value ?? "R.O.",
            SubCurrency = settings.FirstOrDefault(s => s.Key == "subCurrency")?.Value ?? "Bz"
        };
        return Ok(ApiResponse<object>.Ok(dto));
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] SettingsDto dto)
    {
        var settings = new[]
        {
            new { Key = "brandName", Value = dto.BrandName },
            new { Key = "phones", Value = dto.Phones },
            new { Key = "mainCurrency", Value = dto.MainCurrency },
            new { Key = "subCurrency", Value = dto.SubCurrency }
        };

        var existingSettings = await _db.AppSettings
            .Where(s => settings.Select(x => x.Key).Contains(s.Key))
            .ToDictionaryAsync(s => s.Key);

        foreach (var setting in settings)
        {
            if (existingSettings.TryGetValue(setting.Key, out var existing))
                existing.Value = setting.Value;
            else
                _db.AppSettings.Add(new AppSetting { Key = setting.Key, Value = setting.Value });
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(true, "تم تحديث الإعدادات"));
    }

}
