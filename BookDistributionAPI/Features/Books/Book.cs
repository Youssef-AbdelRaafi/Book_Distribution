namespace BookDistributionAPI.Features.Books;

public class Book
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty; 
    public string Subject { get; set; } = string.Empty;
    public int SemesterId { get; set; }
    public Semesters.Semester Semester { get; set; } = null!;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; } 
    public ICollection<LibraryBook> LibraryBooks { get; set; } = new List<LibraryBook>();
}

public class LibraryBook
{
    public int Id { get; set; }
    public int LibraryId { get; set; }
    public Libraries.Library Library { get; set; } = null!;
    public int BookId { get; set; }
    public Book Book { get; set; } = null!;
    public int Quantity { get; set; }
}
