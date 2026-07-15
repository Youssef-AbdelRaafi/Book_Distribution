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
            var response = new LoginResponse
            {
                Success = true,
                Token = CreateToken(user.Username, user.Role, expiresAt),
                ExpiresAt = expiresAt,
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

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive, cancellationToken);
        if (user == null)
            return Unauthorized(ApiResponse<object>.Fail("المستخدم غير موجود"));

        if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return BadRequest(ApiResponse<object>.Fail("كلمة المرور الحالية غير صحيحة"));

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { }, "تم تغيير كلمة المرور بنجاح"));
    }

    private async Task<User?> IsValidUser(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive, cancellationToken);
        if (user == null) return null;
        return PasswordHasher.Verify(request.Password, user.PasswordHash) ? user : null;
    }

    private string CreateToken(string username, string role, DateTime expiresAt)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role)
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
