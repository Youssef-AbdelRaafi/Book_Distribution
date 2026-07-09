namespace BookDistributionAPI.Features.Semesters;

public class Semester
{
    public int Id { get; set; }
    public int AcademicYearId { get; set; }
    public AcademicYears.AcademicYear AcademicYear { get; set; } = null!;
    public string Name { get; set; } = string.Empty; 
    public string Code { get; set; } = string.Empty; 
    public bool IsActive { get; set; }
    public ICollection<Books.Book> Books { get; set; } = new List<Books.Book>();
    public ICollection<Invoices.Invoice> Invoices { get; set; } = new List<Invoices.Invoice>();
}
