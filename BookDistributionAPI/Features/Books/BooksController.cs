using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;

namespace BookDistributionAPI.Features.Books;

[ApiController]
[Route("api/books")]
public class BooksController : ControllerBase
{
    private readonly AppDbContext _db;
    public BooksController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? semesterId)
    {
        var query = _db.Books.AsQueryable();
        if (semesterId.HasValue)
            query = query.Where(b => b.SemesterId == semesterId.Value);

        var books = await query
            .OrderBy(b => b.Grade)
            .ThenBy(b => b.Name)
            .Select(b => new BookDto
            {
                Id = b.Id,
                Name = b.Name,
                Grade = b.Grade,
                Subject = b.Subject,
                SemesterId = b.SemesterId,
                Price = b.Price,
                StockQuantity = b.StockQuantity
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(books));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var book = await _db.Books.FindAsync(id);
        if (book == null) return NotFound(ApiResponse<object>.Fail("الكتاب غير موجود"));
        return Ok(ApiResponse<object>.Ok(new BookDto
        {
            Id = book.Id, Name = book.Name, Grade = book.Grade,
            Subject = book.Subject, SemesterId = book.SemesterId,
            Price = book.Price, StockQuantity = book.StockQuantity
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookDto dto)
    {
        if (!await _db.Semesters.AnyAsync(s => s.Id == dto.SemesterId))
            return BadRequest(ApiResponse<object>.Fail("الفصل الدراسي غير موجود"));

        if (await _db.Books.AnyAsync(b => b.SemesterId == dto.SemesterId && b.Name == dto.Name && b.Grade == dto.Grade))
            return Conflict(ApiResponse<object>.Fail("هذا الكتاب موجود بالفعل في نفس الفصل الدراسي"));

        var book = new Book
        {
            Name = dto.Name,
            Grade = dto.Grade,
            Subject = dto.Subject,
            SemesterId = dto.SemesterId,
            Price = dto.Price,
            StockQuantity = dto.StockQuantity
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new BookDto
        {
            Id = book.Id, Name = book.Name, Grade = book.Grade,
            Subject = book.Subject, SemesterId = book.SemesterId,
            Price = book.Price, StockQuantity = book.StockQuantity
        }, "تم إضافة الكتاب بنجاح"));
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> CreateBulk([FromBody] BulkCreateBooksDto dto)
    {
        var semesterIds = dto.Books.Select(b => b.SemesterId).Distinct().ToList();
        var existingSemesterIds = await _db.Semesters
            .Where(s => semesterIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync();

        var missingSemesterIds = semesterIds.Except(existingSemesterIds).ToList();
        if (missingSemesterIds.Count > 0)
            return BadRequest(ApiResponse<object>.Fail($"فصول دراسية غير موجودة: {string.Join(", ", missingSemesterIds)}"));

        var duplicateInRequest = dto.Books
            .GroupBy(b => new { b.SemesterId, b.Name, b.Grade })
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateInRequest != null)
            return BadRequest(ApiResponse<object>.Fail($"يوجد كتاب مكرر في الطلب: {duplicateInRequest.Key.Name}"));

        var existingBooks = await _db.Books
            .Where(b => semesterIds.Contains(b.SemesterId))
            .Select(b => new { b.SemesterId, b.Name, b.Grade })
            .ToListAsync();

        var existingBookKeys = existingBooks
            .Select(b => new { b.SemesterId, b.Name, b.Grade })
            .ToHashSet();

        foreach (var candidate in dto.Books)
        {
            var key = new { candidate.SemesterId, candidate.Name, candidate.Grade };
            if (existingBookKeys.Contains(key))
                return Conflict(ApiResponse<object>.Fail($"كتاب موجود بالفعل: {candidate.Name}"));
        }

        var books = dto.Books.Select(b => new Book
        {
            Name = b.Name,
            Grade = b.Grade,
            Subject = b.Subject,
            SemesterId = b.SemesterId,
            Price = b.Price,
            StockQuantity = b.StockQuantity
        }).ToList();

        _db.Books.AddRange(books);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(books.Count, $"تم إضافة {books.Count} كتاب بنجاح"));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateBookDto dto)
    {
        var book = await _db.Books.FindAsync(id);
        if (book == null) return NotFound(ApiResponse<object>.Fail("الكتاب غير موجود"));

        if (dto.Name != null) book.Name = dto.Name;
        if (dto.Grade != null) book.Grade = dto.Grade;
        if (dto.Subject != null) book.Subject = dto.Subject;
        if (dto.Price.HasValue && dto.Price.Value != book.Price)
        {
            var hasInvoiceItems = await _db.InvoiceItems.AnyAsync(ii => ii.BookId == id);
            if (hasInvoiceItems)
                return BadRequest(ApiResponse<object>.Fail("لا يمكن تغيير سعر كتاب تم استخدامه في فواتير. أضف كتاباً جديداً بالسعر الجديد"));

            book.Price = dto.Price.Value;
        }
        if (dto.StockQuantity.HasValue && dto.StockQuantity.Value != book.StockQuantity)
        {
            var hasInvoiceItems = await _db.InvoiceItems.AnyAsync(ii => ii.BookId == id);
            if (hasInvoiceItems && dto.StockQuantity.Value < book.StockQuantity)
                return BadRequest(ApiResponse<object>.Fail("لا يمكن تقليل المخزون يدوياً لكتاب له فواتير. استخدم فواتير البيع والمرتجعات"));

            if (dto.StockQuantity.Value < 0)
                return BadRequest(ApiResponse<object>.Fail("المخزون لا يمكن أن يكون سالباً"));

            book.StockQuantity = dto.StockQuantity.Value;
        }

        var duplicate = await _db.Books.AnyAsync(b =>
            b.Id != id &&
            b.SemesterId == book.SemesterId &&
            b.Name == book.Name &&
            b.Grade == book.Grade);

        if (duplicate)
            return Conflict(ApiResponse<object>.Fail("يوجد كتاب آخر بنفس الاسم والصف في نفس الفصل الدراسي"));

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new BookDto
        {
            Id = book.Id, Name = book.Name, Grade = book.Grade,
            Subject = book.Subject, SemesterId = book.SemesterId,
            Price = book.Price, StockQuantity = book.StockQuantity
        }, "تم تحديث الكتاب بنجاح"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var book = await _db.Books.FindAsync(id);
        if (book == null) return NotFound(ApiResponse<object>.Fail("الكتاب غير موجود"));

        var hasInvoices = await _db.InvoiceItems.AnyAsync(ii => ii.BookId == id);
        if (hasInvoices)
            return BadRequest(ApiResponse<object>.Fail("لا يمكن حذف كتاب مرتبط بفواتير"));

        var hasLibraryQuantities = await _db.LibraryBooks.AnyAsync(lb => lb.BookId == id);
        if (hasLibraryQuantities)
            return BadRequest(ApiResponse<object>.Fail("لا يمكن حذف كتاب مرتبط بكميات مكتبات"));

        _db.Books.Remove(book);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(true, "تم حذف الكتاب"));
    }

    [HttpPost("reset-stock")]
    public async Task<IActionResult> ResetStock([FromBody] ResetStockDto dto)
    {
        var semester = await _db.Semesters.FindAsync(dto.SemesterId);
        if (semester == null) return NotFound(ApiResponse<object>.Fail("الفصل الدراسي غير موجود"));

        var hasActiveInvoices = await _db.Invoices
            .AnyAsync(i => i.SemesterId == dto.SemesterId && i.Type != "clearance");

        if (hasActiveInvoices)
            return BadRequest(ApiResponse<object>.Fail("لا يمكن تفريغ المخزون لوجود فواتير نشطة في هذا الفصل الدراسي. قم بإنشاء المخالصات أولاً"));

        var affectedRows = await _db.Books
            .Where(b => b.SemesterId == dto.SemesterId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(b => b.StockQuantity, dto.NewStockQuantity ?? 0));

        return Ok(ApiResponse<object>.Ok(affectedRows, $"تم تحديث مخزون {affectedRows} كتاب بنجاح"));
    }
}
