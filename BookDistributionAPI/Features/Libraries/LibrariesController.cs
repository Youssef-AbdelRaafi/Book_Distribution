using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;
using System.Buffers;


namespace BookDistributionAPI.Features.Libraries;

[ApiController]
[Authorize]
[Route("api/libraries")]
public class LibrariesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IAcademicYearHelper _academicYearHelper;
    private readonly IConfiguration _configuration;

    public LibrariesController(AppDbContext db, IWebHostEnvironment env, IAcademicYearHelper academicYearHelper, IConfiguration configuration)
    {
        _db = db;
        _env = env;
        _academicYearHelper = academicYearHelper;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var query = includeDeleted
            ? _db.Libraries.IgnoreQueryFilters()
            : _db.Libraries.AsQueryable();

        query = query
            .Include(l => l.Governorate)
            .Include(l => l.City);

        if (!includeDeleted)
            query = query.Where(l => l.IsActive);

        var libraries = await query
            .OrderBy(l => l.Governorate.Name)
            .ThenBy(l => l.Name)
            .Select(l => MapToDto(l))
            .ToListAsync(cancellationToken);
            
        return Ok(ApiResponse<object>.Ok(libraries));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var lib = await _db.Libraries
            .Include(l => l.Governorate)
            .Include(l => l.City)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));
        return Ok(ApiResponse<object>.Ok(MapToDto(lib)));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateLibraryDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(ApiResponse<object>.Fail("الرجاء إدخال اسم المكتبة"));

        var validationError = await ValidateLocationAsync(dto.GovernorateId, dto.CityId, cancellationToken);
        if (validationError != null)
            return BadRequest(ApiResponse<object>.Fail(validationError));

        if (await _db.Libraries.AnyAsync(l =>
            l.IsActive &&
            l.GovernorateId == dto.GovernorateId &&
            l.CityId == dto.CityId &&
            l.Name == dto.Name, cancellationToken))
            return Conflict(ApiResponse<object>.Fail("هذه المكتبة موجودة بالفعل في نفس الولاية"));

        var library = new Library
        {
            Name = dto.Name,
            GovernorateId = dto.GovernorateId,
            CityId = dto.CityId,
            Logo = dto.Logo,
            OwnerName = dto.OwnerName ?? string.Empty,
            OwnerPhone = dto.OwnerPhone ?? string.Empty,
            ResponsibleName = dto.ResponsibleName ?? string.Empty,
            ResponsiblePhone = dto.ResponsiblePhone ?? string.Empty,
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
        await _db.SaveChangesAsync(cancellationToken);

        await _db.Entry(library).Reference(l => l.Governorate).LoadAsync(cancellationToken);
        await _db.Entry(library).Reference(l => l.City).LoadAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(MapToDto(library), "تم حفظ المكتبة بنجاح"));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateLibraryDto dto, CancellationToken cancellationToken)
    {
        var lib = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));

        var validationError = await ValidateLocationAsync(dto.GovernorateId, dto.CityId, cancellationToken);
        if (validationError != null)
            return BadRequest(ApiResponse<object>.Fail(validationError));

        if (await _db.Libraries.AnyAsync(l =>
            l.Id != id &&
            l.IsActive &&
            l.GovernorateId == dto.GovernorateId &&
            l.CityId == dto.CityId &&
            l.Name == dto.Name, cancellationToken))
            return Conflict(ApiResponse<object>.Fail("هذه المكتبة موجودة بالفعل في نفس الولاية"));

        lib.Name = dto.Name;
        lib.GovernorateId = dto.GovernorateId;
        lib.CityId = dto.CityId;
        lib.Logo = dto.Logo;
        lib.OwnerName = dto.OwnerName ?? string.Empty;
        lib.OwnerPhone = dto.OwnerPhone ?? string.Empty;
        lib.ResponsibleName = dto.ResponsibleName ?? string.Empty;
        lib.ResponsiblePhone = dto.ResponsiblePhone ?? string.Empty;
        lib.LandlinePhone = dto.LandlinePhone;
        lib.Shift1Start = dto.Shift1Start;
        lib.Shift1End = dto.Shift1End;
        lib.Shift2Start = dto.Shift2Start;
        lib.Shift2End = dto.Shift2End;
        lib.ResponseRating = dto.ResponseRating;
        lib.PaymentRating = dto.PaymentRating;
        lib.Notes = dto.Notes;

        await _db.SaveChangesAsync(cancellationToken);

        await _db.Entry(lib).Reference(l => l.Governorate).LoadAsync(cancellationToken);
        await _db.Entry(lib).Reference(l => l.City).LoadAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(MapToDto(lib), "تم تحديث بيانات المكتبة بنجاح"));
    }

    [HttpPut("{id}/rating")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateRating(int id, [FromBody] UpdateLibraryRatingDto dto, CancellationToken cancellationToken)
    {
        var lib = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));

        lib.ResponseRating = dto.ResponseRating;
        lib.PaymentRating = dto.PaymentRating;
        lib.Notes = dto.Notes;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(true, "تم تحديث تقييم المكتبة"));
    }

    [HttpPost("{id}/logo")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(int id, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        var lib = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));

        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("الرجاء اختيار صورة"));

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(ApiResponse<object>.Fail("حجم الصورة يجب ألا يتجاوز 5 ميجابايت"));

        var fileName = file.FileName;
        if (string.IsNullOrEmpty(fileName))
            return BadRequest(ApiResponse<object>.Fail("اسم الملف غير صالح"));

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        if (!allowedExtensions.Contains(ext))
            return BadRequest(ApiResponse<object>.Fail("صيغة الملف غير مدعومة. الصيغ المسموحة: jpg, png, gif, webp"));

        // Validate file content (magic bytes)
        var header = ArrayPool<byte>.Shared.Rent(12);
        int bytesRead;
        await using (var stream = file.OpenReadStream())
        {
            bytesRead = await stream.ReadAsync(header.AsMemory(0, 12), cancellationToken);
        }
        var isImage = bytesRead >= 2 && header[0] == 0xFF && header[1] == 0xD8 // JPEG
            || bytesRead >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 // PNG
            || bytesRead >= 4 && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 // GIF
            || bytesRead >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
                && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50; // WEBP
        ArrayPool<byte>.Shared.Return(header);
        if (!isImage)
            return BadRequest(ApiResponse<object>.Fail("الملف المرفوع ليس صورة صالحة"));

        var uploadsRoot = GetUploadsRoot();
        var uploadsDir = Path.Combine(uploadsRoot, "logos");
        Directory.CreateDirectory(uploadsDir);
        var savedFileName = $"lib_{id}_{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploadsDir, savedFileName);

        await using (var fileStream = new FileStream(filePath, FileMode.Create))
        await using (var uploadedStream = file.OpenReadStream())
        {
            if (uploadedStream.CanSeek)
                uploadedStream.Position = 0;
            await uploadedStream.CopyToAsync(fileStream, cancellationToken);
        }

        var relativePath = $"/uploads/logos/{savedFileName}";
        TryDeleteExistingLogo(lib.Logo);
        lib.Logo = relativePath;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { logo = relativePath }, "تم رفع الشعار بنجاح"));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var lib = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));

        lib.IsActive = false; 
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(true, "تم حذف المكتبة بنجاح"));
    }

    [HttpPut("{id}/restore")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Restore(int id, CancellationToken cancellationToken)
    {
        var lib = await _db.Libraries.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));

        lib.IsActive = true; 
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(true, "تم استعادة المكتبة بنجاح"));
    }

    [HttpGet("{id}/books")]
    public async Task<IActionResult> GetLibraryBooks(int id, [FromQuery] int? semesterId, CancellationToken cancellationToken)
    {
        var lib = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lib == null) return NotFound(ApiResponse<object>.Fail("المكتبة غير موجودة"));

        var query = _db.Books.AsQueryable();
        if (semesterId.HasValue)
        {
            query = query.Where(b => b.SemesterId == semesterId.Value);
        }
        else
        {
            var activeSemesterIds = await _academicYearHelper.GetActiveSemesterIdsAsync(cancellationToken);
            if (activeSemesterIds.Count > 0)
                query = query.Where(b => activeSemesterIds.Contains(b.SemesterId));
        }

        var books = await query.OrderBy(b => b.Grade).ThenBy(b => b.Name).ToListAsync(cancellationToken);

        var libraryBooks = await _db.LibraryBooks
            .Where(lb => lb.LibraryId == id)
            .ToDictionaryAsync(lb => lb.BookId, cancellationToken);

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
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateLibraryBooks(int id, [FromBody] Books.UpdateLibraryBooksDto dto, CancellationToken cancellationToken)
    {
        var lib = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
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
            .ToListAsync(cancellationToken);

        var missingBookIds = bookIds.Except(existingBookIds).ToList();
        if (missingBookIds.Count > 0)
            return BadRequest(ApiResponse<object>.Fail($"كتب غير موجودة: {string.Join(", ", missingBookIds)}"));

        var existingLibraryBooks = await _db.LibraryBooks
            .Where(lb => lb.LibraryId == id && bookIds.Contains(lb.BookId))
            .ToDictionaryAsync(lb => lb.BookId, cancellationToken);

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

        await _db.SaveChangesAsync(cancellationToken);
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

    private async Task<string?> ValidateLocationAsync(int governorateId, int cityId, CancellationToken cancellationToken)
    {
        var city = await _db.Cities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cityId, cancellationToken);

        if (city == null)
            return "المدينة غير موجودة";

        if (city.GovernorateId != governorateId)
            return "الولاية لا تتبع المحافظة المختارة";

        return null;
    }

    private string GetUploadsRoot()
    {
        var configuredPath = _configuration["App:UploadsPath"] ?? Environment.GetEnvironmentVariable("APP_UPLOADS_DIR");
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        var dataRoot = _configuration["App:DataDirectory"]
            ?? Environment.GetEnvironmentVariable("APP_DATA_DIR")
            ?? Path.Combine(_env.ContentRootPath, "data");
        return Path.Combine(dataRoot, "uploads");
    }

    private void TryDeleteExistingLogo(string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath) || !logoPath.StartsWith("/uploads/logos/", StringComparison.OrdinalIgnoreCase))
            return;

        var fileName = Path.GetFileName(logoPath);
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        var fullPath = Path.Combine(GetUploadsRoot(), "logos", fileName);
        if (System.IO.File.Exists(fullPath))
            System.IO.File.Delete(fullPath);
    }
}
