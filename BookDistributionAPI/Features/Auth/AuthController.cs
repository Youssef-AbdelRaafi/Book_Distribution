using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using BookDistributionAPI.Common;
using BookDistributionAPI.Data;
using BookDistributionAPI.Features.Users;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BookDistributionAPI.Features.Auth;

[ApiController]
[Authorize]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthOptions _authOptions;
    private readonly AppDbContext _db;
    private readonly LoginRateLimiter _rateLimiter;

    public AuthController(IOptions<AuthOptions> authOptions, AppDbContext db, LoginRateLimiter rateLimiter)
    {
        _authOptions = authOptions.Value;
        _db = db;
        _rateLimiter = rateLimiter;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(ApiResponse<LoginResponse>.Fail("الرجاء إدخال اسم المستخدم وكلمة المرور"));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var rateKey = $"login:{request.Username}:{ip}";

        if (_rateLimiter.IsBlocked(rateKey))
            return StatusCode(429, ApiResponse<LoginResponse>.Fail("محاولات كثيرة جداً. الرجاء الانتظار 15 دقيقة"));

        var user = await IsValidUser(request, cancellationToken);
        if (user != null)
        {
            _rateLimiter.Reset(rateKey);
            var expiresAt = DateTime.UtcNow.AddMinutes(_authOptions.TokenMinutes);
            var isGuest = user.TenantId == 2 || user.Role == "Guest";
            var response = new LoginResponse
            {
                Success = true,
                Token = CreateToken(user.Username, user.Role, user.TenantId, expiresAt),
                ExpiresAt = expiresAt,
                TenantId = user.TenantId,
                IsGuest = isGuest,
                Message = "تم تسجيل الدخول بنجاح"
            };
            return Ok(ApiResponse<LoginResponse>.Ok(response, "تم تسجيل الدخول بنجاح"));
        }

        _rateLimiter.RecordAttempt(rateKey);
        return Unauthorized(ApiResponse<LoginResponse>.Fail("اسم المستخدم أو كلمة المرور غير صحيحة"));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(ApiResponse<object>.Fail("الرجاء إدخال كلمة المرور الحالية والجديدة"));

        if (request.NewPassword.Length < 6)
            return BadRequest(ApiResponse<object>.Fail("يجب أن تكون كلمة المرور الجديدة 6 أحرف على الأقل"));

        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
            return Unauthorized(ApiResponse<object>.Fail("غير مصرح به"));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var rateKey = $"change-password:{username}:{ip}";

        if (_rateLimiter.IsBlocked(rateKey))
            return StatusCode(429, ApiResponse<object>.Fail("محاولات كثيرة جداً. الرجاء الانتظار 15 دقيقة"));

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive, cancellationToken);
        if (user == null)
            return Unauthorized(ApiResponse<object>.Fail("المستخدم غير موجود"));

        if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            _rateLimiter.RecordAttempt(rateKey);
            return BadRequest(ApiResponse<object>.Fail("كلمة المرور الحالية غير صحيحة"));
        }

        _rateLimiter.Reset(rateKey);
        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { }, "تم تغيير كلمة المرور بنجاح"));
    }

    [HttpPost("reset-guest")]
    [Authorize]
    public async Task<IActionResult> ResetGuestData(CancellationToken cancellationToken)
    {
        var tenantClaim = User.FindFirst("TenantId")?.Value;
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
        if (tenantClaim != "2" && roleClaim != "Guest" && roleClaim != "Admin")
        {
            return BadRequest(ApiResponse<object>.Fail("عملية إعادة الضبط مخصصة لحساب الزوار فقط"));
        }

        const int guestTenantId = 2;

        var invoices = await _db.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == guestTenantId).ToListAsync(cancellationToken);
        _db.Invoices.RemoveRange(invoices);

        var vouchers = await _db.ReceiptVouchers.IgnoreQueryFilters().Where(rv => rv.TenantId == guestTenantId).ToListAsync(cancellationToken);
        _db.ReceiptVouchers.RemoveRange(vouchers);

        var libraries = await _db.Libraries.IgnoreQueryFilters().Where(l => l.TenantId == guestTenantId).ToListAsync(cancellationToken);
        _db.Libraries.RemoveRange(libraries);

        var books = await _db.Books.IgnoreQueryFilters().Where(b => b.TenantId == guestTenantId).ToListAsync(cancellationToken);
        _db.Books.RemoveRange(books);

        var settings = await _db.AppSettings.IgnoreQueryFilters().Where(s => s.TenantId == guestTenantId).ToListAsync(cancellationToken);
        _db.AppSettings.RemoveRange(settings);

        await _db.SaveChangesAsync(cancellationToken);

        await SeedData.SeedGuestDemoDataAsync(_db, cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { }, "تمت إعادة ضبط البيانات التجريبية لحساب الزوار بنجاح"));
    }

    private async Task<User?> IsValidUser(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive, cancellationToken);
        if (user == null) return null;
        return PasswordHasher.Verify(request.Password, user.PasswordHash) ? user : null;
    }

    private string CreateToken(string username, string role, int tenantId, DateTime expiresAt)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim("TenantId", tenantId.ToString()),
            new Claim("IsGuest", (tenantId == 2 || role == "Guest").ToString().ToLower())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_authOptions.JwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _authOptions.JwtIssuer,
            audience: _authOptions.JwtAudience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
