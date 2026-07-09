using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;

namespace BookDistributionAPI.Features.Libraries;

[ApiController]
[Route("api/libraries")]
public class LibrariesController : ControllerBase
{
    private readonly AppDbContext _db;
    public LibrariesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var libraries = await _db.Libraries
            .Include(l => l.Governorate)
            .Include(l => l.City)
            .Where(l => l.IsActive)
            .OrderBy(l => l.Governorate.Name)
            .ThenBy(l => l.Name)
            .Select(l => MapToDto(l))
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(libraries));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var lib = await _db.Libraries
            .Include(l => l.Governorate)
            .Include(l => l.City)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));
        return Ok(ApiResponse<object>.Ok(MapToDto(lib)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLibraryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(ApiResponse<object>.Fail("الرجاء إدخال اسم المكتبة"));

        var validationError = await ValidateLocationAsync(dto.GovernorateId, dto.CityId);
        if (validationError != null)
            return BadRequest(ApiResponse<object>.Fail(validationError));

        if (await _db.Libraries.AnyAsync(l =>
            l.IsActive &&
            l.GovernorateId == dto.GovernorateId &&
            l.CityId == dto.CityId &&
            l.Name == dto.Name))
            return Conflict(ApiResponse<object>.Fail("هذه المكتبة موجودة بالفعل في نفس الولاية"));

        var library = new Library
        {
            Name = dto.Name,
            GovernorateId = dto.GovernorateId,
            CityId = dto.CityId,
            Logo = dto.Logo,
            OwnerName = dto.OwnerName,
            OwnerPhone = dto.OwnerPhone,
            ResponsibleName = dto.ResponsibleName,
            ResponsiblePhone = dto.ResponsiblePhone,
            LandlinePhone = dto.LandlinePhone,
            Shift1Start = dto.Shift1Start,
            Shift1End = dto.Shift1End,
            Shift2Start = dto.Shift2Start,
            Shift2End = dto.Shift2End,
            ResponseRating = dto.ResponseRating,
            PaymentRating = dto.PaymentRating,
            Notes = dto.Notes,
            IsActive = true
        };

        _db.Libraries.Add(library);
        await _db.SaveChangesAsync();

        await _db.Entry(library).Reference(l => l.Governorate).LoadAsync();
        await _db.Entry(library).Reference(l => l.City).LoadAsync();

        return Ok(ApiResponse<object>.Ok(MapToDto(library), "تم حفظ المكتبة بنجاح"));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateLibraryDto dto)
    {
        var lib = await _db.Libraries.FindAsync(id);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));

        var validationError = await ValidateLocationAsync(dto.GovernorateId, dto.CityId);
        if (validationError != null)
            return BadRequest(ApiResponse<object>.Fail(validationError));

        if (await _db.Libraries.AnyAsync(l =>
            l.Id != id &&
            l.IsActive &&
            l.GovernorateId == dto.GovernorateId &&
            l.CityId == dto.CityId &&
            l.Name == dto.Name))
            return Conflict(ApiResponse<object>.Fail("هذه المكتبة موجودة بالفعل في نفس الولاية"));

        lib.Name = dto.Name;
        lib.GovernorateId = dto.GovernorateId;
        lib.CityId = dto.CityId;
        lib.Logo = dto.Logo;
        lib.OwnerName = dto.OwnerName;
        lib.OwnerPhone = dto.OwnerPhone;
        lib.ResponsibleName = dto.ResponsibleName;
        lib.ResponsiblePhone = dto.ResponsiblePhone;
        lib.LandlinePhone = dto.LandlinePhone;
        lib.Shift1Start = dto.Shift1Start;
        lib.Shift1End = dto.Shift1End;
        lib.Shift2Start = dto.Shift2Start;
        lib.Shift2End = dto.Shift2End;
        lib.ResponseRating = dto.ResponseRating;
        lib.PaymentRating = dto.PaymentRating;
        lib.Notes = dto.Notes;

        await _db.SaveChangesAsync();

        await _db.Entry(lib).Reference(l => l.Governorate).LoadAsync();
        await _db.Entry(lib).Reference(l => l.City).LoadAsync();

        return Ok(ApiResponse<object>.Ok(MapToDto(lib), "تم تحديث بيانات المكتبة بنجاح"));
    }

    [HttpPut("{id}/rating")]
    public async Task<IActionResult> UpdateRating(int id, [FromBody] UpdateLibraryRatingDto dto)
    {
        var lib = await _db.Libraries.FindAsync(id);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));

        lib.ResponseRating = dto.ResponseRating;
        lib.PaymentRating = dto.PaymentRating;
        lib.Notes = dto.Notes;
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(true, "تم تحديث تقييم المكتبة"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var lib = await _db.Libraries.FindAsync(id);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));

        lib.IsActive = false; 
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(true, "تم حذف المكتبة بنجاح"));
    }

    [HttpGet("{id}/books")]
    public async Task<IActionResult> GetLibraryBooks(int id, [FromQuery] int? semesterId)
    {
        var lib = await _db.Libraries.FindAsync(id);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));

        var query = _db.Books.AsQueryable();
        if (semesterId.HasValue)
            query = query.Where(b => b.SemesterId == semesterId.Value);
        else
        {
            var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
            if (activeSemester != null)
                query = query.Where(b => b.SemesterId == activeSemester.Id);
        }

        var books = await query.OrderBy(b => b.Grade).ThenBy(b => b.Name).ToListAsync();

        var libraryBooks = await _db.LibraryBooks
            .Where(lb => lb.LibraryId == id)
            .ToDictionaryAsync(lb => lb.BookId);

        var result = books.Select(b =>
        {
            libraryBooks.TryGetValue(b.Id, out var lb);
            return new Books.LibraryBookDto
            {
                Id = lb?.Id ?? 0,
                LibraryId = id,
                BookId = b.Id,
                BookName = b.Name,
                BookGrade = b.Grade,
                BookPrice = b.Price,
                Quantity = lb?.Quantity ?? 0
            };
        }).ToList();

        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpPut("{id}/books")]
    public async Task<IActionResult> UpdateLibraryBooks(int id, [FromBody] Books.UpdateLibraryBooksDto dto)
    {
        var lib = await _db.Libraries.FindAsync(id);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));
        if (!lib.IsActive) return BadRequest(ApiResponse<object>.Fail("لا يمكن تحديث كميات مكتبة غير نشطة"));

        var items = dto.Items
            .GroupBy(i => i.BookId)
            .Select(g => new Books.LibraryBookItemDto
            {
                BookId = g.Key,
                Quantity = g.Sum(x => x.Quantity)
            })
            .ToList();

        var bookIds = items.Select(i => i.BookId).ToList();
        var existingBookIds = await _db.Books
            .Where(b => bookIds.Contains(b.Id))
            .Select(b => b.Id)
            .ToListAsync();

        var missingBookIds = bookIds.Except(existingBookIds).ToList();
        if (missingBookIds.Count > 0)
            return BadRequest(ApiResponse<object>.Fail($"كتب غير موجودة: {string.Join(", ", missingBookIds)}"));

        var existingLibraryBooks = await _db.LibraryBooks
            .Where(lb => lb.LibraryId == id && bookIds.Contains(lb.BookId))
            .ToDictionaryAsync(lb => lb.BookId);

        foreach (var item in items)
        {
            if (existingLibraryBooks.TryGetValue(item.BookId, out var existing))
            {
                if (item.Quantity == 0)
                    _db.LibraryBooks.Remove(existing);
                else
                    existing.Quantity = item.Quantity;
            }
            else if (item.Quantity > 0)
            {
                _db.LibraryBooks.Add(new Books.LibraryBook
                {
                    LibraryId = id,
                    BookId = item.BookId,
                    Quantity = item.Quantity
                });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(true, "تم تحديث كميات المكتبة بنجاح"));
    }

    private static LibraryDto MapToDto(Library l) => new()
    {
        Id = l.Id,
        Name = l.Name,
        GovernorateId = l.GovernorateId,
        GovernorateName = l.Governorate?.Name ?? "",
        CityId = l.CityId,
        CityName = l.City?.Name ?? "",
        Logo = l.Logo,
        OwnerName = l.OwnerName,
        OwnerPhone = l.OwnerPhone,
        ResponsibleName = l.ResponsibleName,
        ResponsiblePhone = l.ResponsiblePhone,
        LandlinePhone = l.LandlinePhone,
        Shift1Start = l.Shift1Start,
        Shift1End = l.Shift1End,
        Shift2Start = l.Shift2Start,
        Shift2End = l.Shift2End,
        ResponseRating = l.ResponseRating,
        PaymentRating = l.PaymentRating,
        Notes = l.Notes,
        IsActive = l.IsActive
    };

    private async Task<string?> ValidateLocationAsync(int governorateId, int cityId)
    {
        var city = await _db.Cities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cityId);

        if (city == null)
            return "المدينة غير موجودة";

        if (city.GovernorateId != governorateId)
            return "الولاية لا تتبع المحافظة المختارة";

        return null;
    }
}
