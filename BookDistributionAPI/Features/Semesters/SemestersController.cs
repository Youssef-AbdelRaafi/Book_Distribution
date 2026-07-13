using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;
using BookDistributionAPI.Features.AcademicYears;
using BookDistributionAPI.Features.Books;
using System.Threading;

namespace BookDistributionAPI.Features.Semesters;

[ApiController]
[Route("api/semesters")]
public class SemestersController : ControllerBase
{
    private readonly AppDbContext _db;
    public SemestersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var semesters = await _db.Semesters
            .Include(s => s.AcademicYear)
            .OrderByDescending(s => s.AcademicYear.IsActive)
            .ThenBy(s => s.Code)
            .Select(s => new
            {
                s.Id, s.Name, s.Code, s.IsActive,
                s.AcademicYearId,
                AcademicYearName = s.AcademicYear.Name
            })
            .ToListAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(semesters));
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive(CancellationToken cancellationToken)
    {
        var semester = await _db.Semesters
            .Include(s => s.AcademicYear)
            .Where(s => s.IsActive && s.AcademicYear.IsActive)
            .Select(s => new
            {
                s.Id, s.Name, s.Code, s.IsActive,
                s.AcademicYearId,
                AcademicYearName = s.AcademicYear.Name
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (semester == null) return NotFound(ApiResponse<object>.Fail("لا يوجد فصل دراسي نشط"));
        return Ok(ApiResponse<object>.Ok(semester));
    }

    [HttpPut("{id}/activate")]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        var semester = await _db.Semesters.Include(s => s.AcademicYear).FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (semester == null) return NotFound(ApiResponse<object>.Fail("الفصل الدراسي غير موجود"));

        await _db.Semesters
            .Where(s => s.AcademicYearId == semester.AcademicYearId && s.Id != id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), cancellationToken);

        await _db.AcademicYears
            .Where(y => y.Id != semester.AcademicYearId)
            .ExecuteUpdateAsync(y => y.SetProperty(x => x.IsActive, false), cancellationToken);

        await _db.AcademicYears
            .Where(y => y.Id == semester.AcademicYearId)
            .ExecuteUpdateAsync(y => y.SetProperty(x => x.IsActive, true), cancellationToken);

        semester.IsActive = true;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { semester.Id, semester.Name, semester.Code, semester.IsActive }, "تم تنشيط الفصل الدراسي"));
    }

    [HttpPost("start-new-year")]
    public async Task<IActionResult> StartNewYear([FromBody] StartNewYearDto dto, CancellationToken cancellationToken)
    {
        var nextYearName = $"{dto.StartYear}-{dto.StartYear + 1}";
        
        if (await _db.AcademicYears.AnyAsync(y => y.Name == nextYearName, cancellationToken))
            return BadRequest(ApiResponse<object>.Fail("العام الدراسي موجود بالفعل"));

        var activeYear = await _db.AcademicYears
            .Include(y => y.Semesters)
            .FirstOrDefaultAsync(y => y.IsActive, cancellationToken);

        if (activeYear != null)
        {
            var activeSemesterIds = activeYear.Semesters.Select(s => s.Id).ToList();
            var hasOpenOrders = await _db.Invoices.AnyAsync(i =>
                activeSemesterIds.Contains(i.SemesterId) && i.Type != "clearance", cancellationToken);
            if (hasOpenOrders)
                return BadRequest(ApiResponse<object>.Fail("لا يمكن بدء عام جديد. توجد فواتير غير مسددة في العام الحالي. قم بإنشاء المخالصات أولاً."));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var oldBooks = new List<Book>();
        if (activeYear != null)
        {
            var oldSemesterIds = activeYear.Semesters.Select(s => s.Id).ToList();
            oldBooks = await _db.Books.Where(b => oldSemesterIds.Contains(b.SemesterId)).ToListAsync(cancellationToken);
        }

        await _db.AcademicYears.ExecuteUpdateAsync(y => y.SetProperty(x => x.IsActive, false), cancellationToken);
        await _db.Semesters.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), cancellationToken);

        var newYear = new AcademicYear { Name = nextYearName, IsActive = true };
        _db.AcademicYears.Add(newYear);
        await _db.SaveChangesAsync(cancellationToken);

        var semA = new Semester { AcademicYearId = newYear.Id, Name = "الفصل الأول", Code = "A", IsActive = true };
        var semB = new Semester { AcademicYearId = newYear.Id, Name = "الفصل الثاني", Code = "B", IsActive = false };
        _db.Semesters.AddRange(semA, semB);
        await _db.SaveChangesAsync(cancellationToken);

        if (activeYear != null && oldBooks.Any())
        {
            var oldSemA = activeYear.Semesters.FirstOrDefault(s => s.Code == "A");
            var oldSemB = activeYear.Semesters.FirstOrDefault(s => s.Code == "B");

            var newBooks = new List<Book>();

            foreach (var oldBook in oldBooks)
            {
                var newBook = new Book
                {
                    Name = oldBook.Name,
                    Grade = oldBook.Grade,
                    Subject = oldBook.Subject,
                    Price = oldBook.Price,
                    StockQuantity = 0
                };

                if (oldSemA != null && oldBook.SemesterId == oldSemA.Id)
                    newBook.SemesterId = semA.Id;
                else if (oldSemB != null && oldBook.SemesterId == oldSemB.Id)
                    newBook.SemesterId = semB.Id;
                else
                    continue;

                newBooks.Add(newBook);
            }

            if (newBooks.Any())
            {
                _db.Books.AddRange(newBooks);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { newYear.Id, newYear.Name }, $"تم بدء العام الدراسي الجديد {nextYearName} بنجاح"));
    }
}

public class StartNewYearDto
{
    [Required, Range(2020, 2100)]
    public int StartYear { get; set; }
}
