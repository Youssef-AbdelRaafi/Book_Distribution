using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using BookDistributionAPI.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BookDistributionAPI.Features.Auth;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly AuthOptions _authOptions;

    public AuthController(IOptions<AuthOptions> authOptions)
    {
        _authOptions = authOptions.Value;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new LoginResponse
            {
                Success = false,
                Message = "الرجاء إدخال اسم المستخدم وكلمة المرور"
            });
        }

        if (IsValidUser(request))
        {
            var expiresAt = DateTime.UtcNow.AddMinutes(_authOptions.TokenMinutes);
            return Ok(new LoginResponse
            {
                Success = true,
                Token = CreateToken(request.Username, expiresAt),
                ExpiresAt = expiresAt,
                Message = "تم تسجيل الدخول بنجاح"
            });
        }

        return Unauthorized(new LoginResponse
        {
            Success = false,
            Message = "اسم المستخدم أو كلمة المرور غير صحيحة"
        });
    }

    private bool IsValidUser(LoginRequest request)
    {
        if (!string.Equals(request.Username, _authOptions.AdminUsername, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrWhiteSpace(_authOptions.AdminPasswordHash))
            return false;

        return PasswordHasher.Verify(request.Password, _authOptions.AdminPasswordHash);
    }

    private string CreateToken(string username, DateTime expiresAt)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, "Admin")
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
