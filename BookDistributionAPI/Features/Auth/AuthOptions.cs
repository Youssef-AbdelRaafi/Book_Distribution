using System.Security.Cryptography;

namespace BookDistributionAPI.Features.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string JwtIssuer { get; set; } = "BookDistributionAPI";
    public string JwtAudience { get; set; } = "BookDistributionClient";
    public string JwtSigningKey { get; set; } = string.Empty;
    public int TokenMinutes { get; set; } = 10080;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(JwtSigningKey) || JwtSigningKey.Length < 32)
            throw new InvalidOperationException("Auth:JwtSigningKey must be at least 32 characters.");

        if (TokenMinutes < 15 || TokenMinutes > 43200)
            throw new InvalidOperationException("Auth:TokenMinutes must be between 15 and 43200.");
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
        try
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256")
                return false;

            if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
                return false;

            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            if (salt.Length == 0 || expectedHash.Length == 0)
                return false;

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool IsSupportedHashFormat(string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
            return false;

        var parts = storedHash.Split('$');
        return parts.Length == 4
            && parts[0] == "pbkdf2-sha256"
            && int.TryParse(parts[1], out var iterations)
            && iterations > 0
            && IsBase64(parts[2])
            && IsBase64(parts[3]);
    }

    private static bool IsBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value).Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
