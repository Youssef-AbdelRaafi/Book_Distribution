namespace BookDistributionAPI.Features.Libraries;

public class Library
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GovernorateId { get; set; }
    public Governorates.Governorate Governorate { get; set; } = null!;
    public int CityId { get; set; }
    public Governorates.City City { get; set; } = null!;
    public string? Logo { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerPhone { get; set; } = string.Empty;
    public string ResponsibleName { get; set; } = string.Empty;
    public string ResponsiblePhone { get; set; } = string.Empty;
    public string? LandlinePhone { get; set; }
    public string Shift1Start { get; set; } = "08:00";
    public string Shift1End { get; set; } = "13:00";
    public string? Shift2Start { get; set; } = "16:00";
    public string? Shift2End { get; set; } = "22:00";
    public string? ResponseRating { get; set; }
    public string? PaymentRating { get; set; } 
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Books.LibraryBook> LibraryBooks { get; set; } = new List<Books.LibraryBook>();
    public ICollection<Invoices.Invoice> Invoices { get; set; } = new List<Invoices.Invoice>();
}
