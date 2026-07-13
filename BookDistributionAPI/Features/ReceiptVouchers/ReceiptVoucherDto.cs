using System.ComponentModel.DataAnnotations;

namespace BookDistributionAPI.Features.ReceiptVouchers;

public class ReceiptVoucherDto
{
    public int Id { get; set; }
    public int VoucherNumber { get; set; }
    public int VoucherYear { get; set; }
    public string DisplayNumber { get; set; } = string.Empty;
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public string GovernorateName { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public int? SemesterId { get; set; }
    public string? SemesterName { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? ChequeNumber { get; set; }
    public string? BankName { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateReceiptVoucherDto
{
    [Required, Range(1, int.MaxValue)]
    public int LibraryId { get; set; }

    public int? SemesterId { get; set; }

    [Required, Range(0.001, double.MaxValue, ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر")]
    public decimal Amount { get; set; }

    [Required, RegularExpression("^(cash|cheque)$", ErrorMessage = "طريقة الدفع يجب أن تكون cash أو cheque")]
    public string PaymentMethod { get; set; } = "cash";

    [StringLength(50)]
    public string? ChequeNumber { get; set; }
    [StringLength(100)]
    public string? BankName { get; set; }

    [Required(ErrorMessage = "يجب تحديد الغرض من سند القبض"), StringLength(500)]
    public string Purpose { get; set; } = string.Empty;

    [Required(ErrorMessage = "يجب تحديد تاريخ سند القبض")]
    public DateTime Date { get; set; }
}
