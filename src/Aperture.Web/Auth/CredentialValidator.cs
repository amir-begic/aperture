using Microsoft.AspNetCore.Identity;

namespace Aperture.Web.Auth;

public sealed class CredentialValidator(LoginStore logins)
{
    // Identity's hasher (PBKDF2, salted). It ignores its "user" argument.
    private static readonly PasswordHasher<string> Hasher = new();

    public static string HashPassword(string password) => Hasher.HashPassword(string.Empty, password);

    public static bool IsWellFormedHash(string hash) =>
        !string.IsNullOrWhiteSpace(hash) && Convert.TryFromBase64String(hash, new byte[hash.Length], out _);

    public bool IsValid(string? username, string? password)
    {
        if (logins.Current is not { } login
            || string.IsNullOrEmpty(password)
            || !string.Equals(username?.Trim(), login.Username, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Hasher.VerifyHashedPassword(string.Empty, login.PasswordHash, password)
            != PasswordVerificationResult.Failed;
    }
}
