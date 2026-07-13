using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;
using System.Threading;

namespace BookDistributionAPI.Features.AcademicYears;

[ApiController]
[Route("api/academic-years")]
public class AcademicYearsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AcademicYearsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var years = await _db.AcademicYears
            .OrderByDescending(a => a.IsActive)
            .ThenByDescending(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.IsActive,
                Semesters = a.Semesters.Select(s => new { s.Id, s.Name, s.Code, s.IsActive })
            })
            .ToListAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(years));
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive(CancellationToken cancellationToken)
    {
        var year = await _db.AcademicYears
            .Include(a => a.Semesters)
            .FirstOrDefaultAsync(a => a.IsActive, cancellationToken);
        if (year == null) return NotFound(ApiResponse<object>.Fail("لا يوجد عام دراسي نشط"));

        var dto = new
        {
            year.Id,
            year.Name,
            year.IsActive,
            Semesters = year.Semesters.Select(s => new { s.Id, s.Name, s.Code, s.IsActive })
        };
        return Ok(ApiResponse<object>.Ok(dto));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAcademicYearDto dto, CancellationToken cancellationToken)
    {
        if (await _db.AcademicYears.AnyAsync(a => a.Name == dto.Name, cancellationToken))
            return Conflict(ApiResponse<object>.Fail("العام الدراسي موجود بالفعل"));

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        await _db.AcademicYears
            .Where(y => y.IsActive)
            .ExecuteUpdateAsync(y => y.SetProperty(x => x.IsActive, false), cancellationToken);

        var year = new AcademicYear
        {
            Name = dto.Name,
            IsActive = true
        };

        _db.AcademicYears.Add(year);
        await _db.SaveChangesAsync(cancellationToken);

        _db.Semesters.AddRange(
            new Semesters.Semester { AcademicYearId = year.Id, Name = "الفصل الأول", Code = "A", IsActive = true },
            new Semesters.Semester { AcademicYearId = year.Id, Name = "الفصل الثاني", Code = "B", IsActive = false }
        );
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { year.Id, year.Name, year.IsActive }, "تم إنشاء العام الدراسي بنجاح"));
    }

    [HttpPut("{id}/activate")]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        var year = await _db.AcademicYears
            .Include(a => a.Semesters)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        
        if (year == null)
            return NotFound(ApiResponse<object>.Fail("العام الدراسي غير موجود"));

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        await _db.AcademicYears.ExecuteUpdateAsync(y => y.SetProperty(x => x.IsActive, false), cancellationToken);
        await _db.Semesters.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), cancellationToken);

        year.IsActive = true;
        await _db.SaveChangesAsync(cancellationToken);

        var firstSemester = year.Semesters.FirstOrDefault();
        if (firstSemester != null)
        {
            firstSemester.IsActive = true;
            await _db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { year.Id, year.Name, year.IsActive }, $"تم تفعيل العام الدراسي {year.Name} بنجاح"));
    }
}
