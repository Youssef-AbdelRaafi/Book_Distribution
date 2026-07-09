using System.Security.Cryptography;

namespace BookDistributionAPI.Features.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string AdminUsername { get; set; } = "admin";
    public string? AdminPasswordHash { get; set; }
    public string JwtIssuer { get; set; } = "BookDistributionAPI";
    public string JwtAudience { get; set; } = "BookDistributionClient";
    public string JwtSigningKey { get; set; } = string.Empty;
    public int TokenMinutes { get; set; } = 480;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AdminUsername))
            throw new InvalidOperationException("Auth:AdminUsername is required.");

        if (string.IsNullOrWhiteSpace(AdminPasswordHash))
            throw new InvalidOperationException("Configure Auth:AdminPasswordHash.");

        if (string.IsNullOrWhiteSpace(JwtSigningKey) || JwtSigningKey.Length < 32)
            throw new InvalidOperationException("Auth:JwtSigningKey must be at least 32 characters.");

        if (TokenMinutes < 15 || TokenMinutes > 1440)
            throw new InvalidOperationException("Auth:TokenMinutes must be between 15 and 1440.");
    }
}

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256")
            return false;

        if (!int.TryParse(parts[1], out var iterations))
            return false;

        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
