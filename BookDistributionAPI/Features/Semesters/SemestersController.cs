using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;
using BookDistributionAPI.Features.AcademicYears;

namespace BookDistributionAPI.Features.Semesters;

[ApiController]
[Route("api/semesters")]
public class SemestersController : ControllerBase
{
    private readonly AppDbContext _db;
    public SemestersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
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
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(semesters));
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
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
            .FirstOrDefaultAsync();
        if (semester == null) return NotFound(ApiResponse<object>.Fail("لا يوجد فصل دراسي نشط"));
        return Ok(ApiResponse<object>.Ok(semester));
    }

    [HttpPut("{id}/activate")]
    public async Task<IActionResult> Activate(int id)
    {
        var semester = await _db.Semesters.Include(s => s.AcademicYear).FirstOrDefaultAsync(s => s.Id == id);
        if (semester == null) return NotFound(ApiResponse<object>.Fail("الفصل الدراسي غير موجود"));

        await _db.Semesters
            .Where(s => s.AcademicYearId == semester.AcademicYearId && s.Id != id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));

        await _db.AcademicYears
            .Where(y => y.Id != semester.AcademicYearId)
            .ExecuteUpdateAsync(y => y.SetProperty(x => x.IsActive, false));

        await _db.AcademicYears
            .Where(y => y.Id == semester.AcademicYearId)
            .ExecuteUpdateAsync(y => y.SetProperty(x => x.IsActive, true));

        semester.IsActive = true;
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { semester.Id, semester.Name, semester.Code, semester.IsActive }, "تم تنشيط الفصل الدراسي"));
    }

    [HttpPost("start-new-year")]
    public async Task<IActionResult> StartNewYear([FromBody] StartNewYearDto dto)
    {
        var nextYearName = $"{dto.StartYear}-{dto.StartYear + 1}";
        
        if (await _db.AcademicYears.AnyAsync(y => y.Name == nextYearName))
            return BadRequest(ApiResponse<object>.Fail("العام الدراسي موجود بالفعل"));

        // Disable all existing years and semesters
        await _db.AcademicYears.ExecuteUpdateAsync(y => y.SetProperty(x => x.IsActive, false));
        await _db.Semesters.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));

        var newYear = new AcademicYear { Name = nextYearName, IsActive = true };
        _db.AcademicYears.Add(newYear);
        await _db.SaveChangesAsync();

        var semA = new Semester { AcademicYearId = newYear.Id, Name = "الفصل الأول", Code = "A", IsActive = true };
        var semB = new Semester { AcademicYearId = newYear.Id, Name = "الفصل الثاني", Code = "B", IsActive = false };
        _db.Semesters.AddRange(semA, semB);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { newYear.Id, newYear.Name }, $"تم بدء العام الدراسي الجديد {nextYearName} بنجاح"));
    }
}

public class StartNewYearDto
{
    public int StartYear { get; set; }
}
