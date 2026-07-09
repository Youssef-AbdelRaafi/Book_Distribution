using System.ComponentModel.DataAnnotations;

namespace BookDistributionAPI.Features.Invoices;

public class InvoiceDto
{
    public int Id { get; set; }
    public int InvoiceNumber { get; set; }
    public int InvoiceYear { get; set; }
    public string TermCode { get; set; } = string.Empty;
    public string DisplayNumber { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public string GovernorateName { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public int SemesterId { get; set; }
    public string SemesterName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal TotalAmount { get; set; }
    public string PrintStatus { get; set; } = string.Empty;
    public string? ResponsibleName { get; set; }
    public string? ResponsiblePhone { get; set; }
    public List<InvoiceItemDto> Items { get; set; } = new();
}

public class InvoiceItemDto
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string BookName { get; set; } = string.Empty;
    public string BookGrade { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}

public class CreateOrderDto
{
    [Range(1, int.MaxValue)]
    public int LibraryId { get; set; }

    [Range(1, int.MaxValue)]
    public int SemesterId { get; set; }

    [Required, MinLength(1)]
    public List<CreateInvoiceItemDto> Items { get; set; } = new();
}

public class CreateRefundDto
{
    [Range(1, int.MaxValue)]
    public int LibraryId { get; set; }

    [Range(1, int.MaxValue)]
    public int SemesterId { get; set; }

    [Required, MinLength(1)]
    public List<CreateInvoiceItemDto> Items { get; set; } = new();
}

public class CreateClearanceDto
{
    [Range(1, int.MaxValue)]
    public int LibraryId { get; set; }

    [Range(1, int.MaxValue)]
    public int SemesterId { get; set; }
}

public class CreateBatchClearanceDto
{
    [Range(1, int.MaxValue)]
    public int SemesterId { get; set; }
}

public class ClearanceBatchResultDto
{
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
    public List<InvoiceDto> Invoices { get; set; } = new();
}

public class ClearancePreviewDto
{
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public string GovernorateName { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public int SemesterId { get; set; }
    public string SemesterName { get; set; } = string.Empty;
    public string TermCode { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string? ResponsibleName { get; set; }
    public string? ResponsiblePhone { get; set; }
    public List<InvoiceItemDto> Items { get; set; } = new();
}

public class CreateInvoiceItemDto
{
    [Range(1, int.MaxValue)]
    public int BookId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}

public class UpdatePrintStatusDto
{
    [Required, RegularExpression("^(pending|printed)$")]
    public string PrintStatus { get; set; } = string.Empty;
}
