using System.Net;
using System.Text.Json;
using Aperture.Web.Configuration;
using Microsoft.Extensions.Options;

namespace Aperture.Web.Auth;

public sealed record Login(string Username, string PasswordHash);

/// <summary>
/// Where the login comes from. The Mac mini configures it (Auth:Username and Auth:PasswordHash, see install.sh). A
/// laptop installation (<see cref="Hosting.LocalOnly"/>) doesn't: there, the first visit creates it in the browser
/// (/setup) and it's kept in data/keys/login.json. A configured login always wins.
/// </summary>
public sealed class LoginStore(IOptions<AuthOptions> options, AperturePaths paths, Hosting hosting)
{
    private readonly Lock _lock = new();
    private Login? _saved;

    public Login? Current
    {
        get
        {
            var configured = options.Value;
            if (!string.IsNullOrWhiteSpace(configured.Username) && CredentialValidator.IsWellFormedHash(configured.PasswordHash))
            {
                return new Login(configured.Username, configured.PasswordHash);
            }

            return hosting.LocalOnly ? Saved() : null;
        }
    }

    /// <summary>Only on a laptop installation, and only until the login exists.</summary>
    public bool CanSetUp => hosting.LocalOnly && Current is null && !File.Exists(paths.LoginFile);

    /// <summary>
    /// <see cref="CanSetUp"/>, for a visitor on this computer. A local-only installation listens on localhost anyway;
    /// this keeps /setup closed should it ever be reachable from elsewhere.
    /// </summary>
    public bool CanSetUpFrom(IPAddress? remote) =>
        CanSetUp && remote is not null && IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote);

    /// <summary>Creates the login; false if there already is one (someone else was quicker, or it's configured).</summary>
    public bool TrySetUp(string username, string password)
    {
        lock (_lock)
        {
            if (!CanSetUp)
            {
                return false;
            }

            var login = new Login(username.Trim(), CredentialValidator.HashPassword(password));
            Directory.CreateDirectory(paths.KeysDirectory);
            var temp = paths.LoginFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(login));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.Move(temp, paths.LoginFile, overwrite: false);
            _saved = login;
            return true;
        }
    }

    private Login? Saved()
    {
        lock (_lock)
        {
            if (_saved is null && File.Exists(paths.LoginFile))
            {
                try
                {
                    _saved = JsonSerializer.Deserialize<Login>(File.ReadAllText(paths.LoginFile)) is { } login
                        && !string.IsNullOrWhiteSpace(login.Username) && CredentialValidator.IsWellFormedHash(login.PasswordHash)
                            ? login
                            : null;
                }
                catch (JsonException)
                {
                    // A damaged file: nobody can sign in, and /setup stays closed while it exists. Delete it to start over.
                    return null;
                }
            }

            return _saved;
        }
    }
}
