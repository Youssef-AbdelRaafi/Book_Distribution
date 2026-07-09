using System.ComponentModel.DataAnnotations;

namespace BookDistributionAPI.Features.Books;

public class BookDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int SemesterId { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
}

public class CreateBookDto
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 2)]
    public string Grade { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 2)]
    public string Subject { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int SemesterId { get; set; }

    [Range(typeof(decimal), "0", "9999999")]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }
}

public class UpdateBookDto
{
    [StringLength(200, MinimumLength = 2)]
    public string? Name { get; set; }

    [StringLength(100, MinimumLength = 2)]
    public string? Grade { get; set; }

    [StringLength(100, MinimumLength = 2)]
    public string? Subject { get; set; }

    [Range(typeof(decimal), "0", "9999999")]
    public decimal? Price { get; set; }

    [Range(0, int.MaxValue)]
    public int? StockQuantity { get; set; }
}

public class BulkCreateBooksDto
{
    [Required, MinLength(1)]
    public List<CreateBookDto> Books { get; set; } = new();
}

public class LibraryBookDto
{
    public int Id { get; set; }
    public int LibraryId { get; set; }
    public int BookId { get; set; }
    public string BookName { get; set; } = string.Empty;
    public string BookGrade { get; set; } = string.Empty;
    public decimal BookPrice { get; set; }
    public int Quantity { get; set; }
}

public class UpdateLibraryBooksDto
{
    [Required]
    public List<LibraryBookItemDto> Items { get; set; } = new();
}

public class LibraryBookItemDto
{
    [Range(1, int.MaxValue)]
    public int BookId { get; set; }

    [Range(0, int.MaxValue)]
    public int Quantity { get; set; }
}

public class ResetStockDto
{
    [Range(1, int.MaxValue)]
    public int SemesterId { get; set; }

    [Range(0, int.MaxValue)]
    public int? NewStockQuantity { get; set; }
}
