namespace BookDistributionAPI.Features.ReceiptVouchers;

public class ReceiptVoucher
{
    public int Id { get; set; }
    public int VoucherNumber { get; set; }
    public int VoucherYear { get; set; }
    public string DisplayNumber => $"{VoucherYear}-{VoucherNumber}";
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public Libraries.Library Library { get; set; } = null!;
    public int? SemesterId { get; set; }
    public Semesters.Semester? Semester { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "cash"; // "cash" or "cheque"
    public string? ChequeNumber { get; set; }
    public string? BankName { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
