using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;

namespace BookDistributionAPI.Features.AcademicYears;

[ApiController]
[Route("api/academic-years")]
public class AcademicYearsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AcademicYearsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var years = await _db.AcademicYears
            .Include(a => a.Semesters)
            .OrderByDescending(a => a.IsActive)
            .ThenByDescending(a => a.Id)
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(years));
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var year = await _db.AcademicYears
            .Include(a => a.Semesters)
            .FirstOrDefaultAsync(a => a.IsActive);
        if (year == null) return NotFound(ApiResponse<object>.Fail("لا يوجد عام دراسي نشط"));
        return Ok(ApiResponse<object>.Ok(year));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAcademicYearDto dto)
    {
        if (await _db.AcademicYears.AnyAsync(a => a.Name == dto.Name))
            return Conflict(ApiResponse<object>.Fail("العام الدراسي موجود بالفعل"));

        await using var transaction = await _db.Database.BeginTransactionAsync();

        await _db.AcademicYears
            .Where(y => y.IsActive)
            .ExecuteUpdateAsync(y => y.SetProperty(x => x.IsActive, false));

        var year = new AcademicYear
        {
            Name = dto.Name,
            IsActive = true
        };

        _db.AcademicYears.Add(year);
        await _db.SaveChangesAsync();

        _db.Semesters.AddRange(
            new Semesters.Semester { AcademicYearId = year.Id, Name = "الأول", Code = "A", IsActive = true },
            new Semesters.Semester { AcademicYearId = year.Id, Name = "الثاني", Code = "B", IsActive = false }
        );
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(ApiResponse<object>.Ok(year, "تم إنشاء العام الدراسي بنجاح"));
    }
}
