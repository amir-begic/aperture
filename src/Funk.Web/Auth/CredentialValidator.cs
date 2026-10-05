using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Funk.Web.Auth;

public sealed class CredentialValidator(IOptions<AuthOptions> options)
{
    // Identity's hasher (PBKDF2, salted). It ignores its "user" argument.
    private static readonly PasswordHasher<string> Hasher = new();

    public static string HashPassword(string password) => Hasher.HashPassword(string.Empty, password);

    public static bool IsWellFormedHash(string hash) =>
        !string.IsNullOrWhiteSpace(hash) && Convert.TryFromBase64String(hash, new byte[hash.Length], out _);

    public bool IsValid(string? username, string? password)
    {
        var auth = options.Value;
        if (string.IsNullOrEmpty(password)
            || !string.Equals(username?.Trim(), auth.Username, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Hasher.VerifyHashedPassword(string.Empty, auth.PasswordHash, password)
            != PasswordVerificationResult.Failed;
    }
}
