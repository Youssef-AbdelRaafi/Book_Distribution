using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Data;

namespace BookDistributionAPI.Common;

public interface IAcademicYearHelper
{
    Task<List<int>> GetActiveSemesterIdsAsync(CancellationToken cancellationToken = default);
}

public class AcademicYearHelper : IAcademicYearHelper
{
    private readonly AppDbContext _db;

    public AcademicYearHelper(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<int>> GetActiveSemesterIdsAsync(CancellationToken cancellationToken = default)
    {
        var activeYear = await _db.AcademicYears
            .AsNoTracking()
            .FirstOrDefaultAsync(y => y.IsActive, cancellationToken);

        if (activeYear == null)
            return new List<int>();

        return await _db.Semesters
            .AsNoTracking()
            .Where(s => s.AcademicYearId == activeYear.Id)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
    }
}
