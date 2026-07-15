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
        return await _db.AcademicYears
            .AsNoTracking()
            .Where(y => y.IsActive)
            .SelectMany(y => y.Semesters.Select(s => s.Id))
            .ToListAsync(cancellationToken);
    }
}
