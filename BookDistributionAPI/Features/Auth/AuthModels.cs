namespace BookDistributionAPI.Features.Auth;

using System.ComponentModel.DataAnnotations;

public class LoginRequest
{
    [Required, StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Message { get; set; }
    public int TenantId { get; set; } = 1;
    public bool IsGuest { get; set; } = false;
}

public class ChangePasswordRequest
{
    [Required, StringLength(200)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string NewPassword { get; set; } = string.Empty;
}
