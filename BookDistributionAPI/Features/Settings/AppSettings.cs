using System.ComponentModel.DataAnnotations;

namespace BookDistributionAPI.Features.Settings;

public class AppSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class SettingsDto
{
    [Required, StringLength(200)]
    public string BrandName { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string Phones { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string MainCurrency { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string SubCurrency { get; set; } = string.Empty;

    [StringLength(200)]
    public string? OwnerSignatureName { get; set; }

    [StringLength(50)]
    public string? WhatsAppNumber { get; set; }
}
