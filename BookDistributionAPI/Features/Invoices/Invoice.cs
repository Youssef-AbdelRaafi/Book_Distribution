namespace BookDistributionAPI.Features.Invoices;

public class Invoice
{
    public int Id { get; set; }
    public int InvoiceNumber { get; set; } 
    public int InvoiceYear { get; set; }
    public string TermCode { get; set; } = string.Empty; 
    public string DisplayNumber 
    { 
        get
        {
            return $"{InvoiceYear}{TermCode}{InvoiceNumber}";
        }
    }
    public string Type { get; set; } = string.Empty; 
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public Libraries.Library Library { get; set; } = null!;
    public int SemesterId { get; set; }
    public Semesters.Semester Semester { get; set; } = null!;
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public decimal TotalAmount { get; set; }
    public string PrintStatus { get; set; } = "pending";
    public string? ResponsibleName { get; set; }
    public string? ResponsiblePhone { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
}

public class InvoiceItem
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public int BookId { get; set; }
    public Books.Book Book { get; set; } = null!;
    public string BookName { get; set; } = string.Empty; 
    public string BookGrade { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; } 
    public decimal Total { get; set; }     
}
