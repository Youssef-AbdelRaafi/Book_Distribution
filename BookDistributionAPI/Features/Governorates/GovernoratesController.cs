using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;


namespace BookDistributionAPI.Features.Governorates;

[ApiController]
[Route("api/governorates")]
[AllowAnonymous]
public class GovernoratesController : ControllerBase
{
    private readonly AppDbContext _db;
    public GovernoratesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var governorates = await _db.Governorates
            .Include(g => g.Cities)
            .OrderBy(g => g.Name)
            .Select(g => new
            {
                g.Id,
                g.Name,
                Cities = g.Cities.OrderBy(c => c.Name).Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.GovernorateId
                })
            })
            .ToListAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(governorates));
    }
}
