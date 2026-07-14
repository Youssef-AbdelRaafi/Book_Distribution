using System.ComponentModel.DataAnnotations;

namespace BookDistributionAPI.Features.Libraries;
public class LibraryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GovernorateId { get; set; }
    public string GovernorateName { get; set; } = string.Empty;
    public int CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public string? Logo { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerPhone { get; set; } = string.Empty;
    public string ResponsibleName { get; set; } = string.Empty;
    public string ResponsiblePhone { get; set; } = string.Empty;
    public string? LandlinePhone { get; set; }
    public string Shift1Start { get; set; } = string.Empty;
    public string Shift1End { get; set; } = string.Empty;
    public string? Shift2Start { get; set; }
    public string? Shift2End { get; set; }
    public string? ResponseRating { get; set; }
    public string? PaymentRating { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}

public class CreateLibraryDto
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int GovernorateId { get; set; }

    [Range(1, int.MaxValue)]
    public int CityId { get; set; }

    [StringLength(1000)]
    public string? Logo { get; set; }

    [StringLength(100)]
    public string? OwnerName { get; set; }

    [StringLength(30)]
    public string? OwnerPhone { get; set; }

    [StringLength(100)]
    public string? ResponsibleName { get; set; }

    [StringLength(30)]
    public string? ResponsiblePhone { get; set; }

    [StringLength(30)]
    public string? LandlinePhone { get; set; }

    [Required, RegularExpression("^([01]\\d|2[0-3]):[0-5]\\d$")]
    public string Shift1Start { get; set; } = "08:00";

    [Required, RegularExpression("^([01]\\d|2[0-3]):[0-5]\\d$")]
    public string Shift1End { get; set; } = "13:00";

    [RegularExpression("^([01]\\d|2[0-3]):[0-5]\\d$")]
    public string? Shift2Start { get; set; } = "16:00";

    [RegularExpression("^([01]\\d|2[0-3]):[0-5]\\d$")]
    public string? Shift2End { get; set; } = "22:00";

    [StringLength(20)]
    public string? ResponseRating { get; set; }

    [StringLength(20)]
    public string? PaymentRating { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class UpdateLibraryDto : CreateLibraryDto { }

public class UpdateLibraryRatingDto
{
    [StringLength(20)]
    public string? ResponseRating { get; set; }

    [StringLength(20)]
    public string? PaymentRating { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}
