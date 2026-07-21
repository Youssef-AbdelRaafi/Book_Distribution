using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BookDistributionAPI.Common.Services;

public interface ICurrentTenantService
{
    int TenantId { get; }
}

public class CurrentTenantService : ICurrentTenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int TenantId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user != null)
            {
                var claim = user.FindFirst("TenantId");
                if (claim != null && int.TryParse(claim.Value, out var tenantId))
                {
                    return tenantId;
                }
            }
            return 1; // Default to Main Account
        }
    }
}
