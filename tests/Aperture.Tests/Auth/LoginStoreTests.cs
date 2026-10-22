using System.Net;
using Aperture.Web.Auth;
using Aperture.Web.Configuration;
using Microsoft.Extensions.Options;

namespace Aperture.Tests.Auth;

public class LoginStoreTests : IDisposable
{
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-login-").FullName;

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    private AperturePaths Paths => new(_dataDirectory);

    private LoginStore Create(bool localOnly, AuthOptions? configured = null) =>
        new(Options.Create(configured ?? new AuthOptions()), Paths, new Hosting(localOnly));

    [Fact]
    public void Laptop_creates_the_login_once_and_keeps_it()
    {
        var store = Create(localOnly: true);
        Assert.True(store.CanSetUp);

        Assert.True(store.TrySetUp(" studio ", "correct horse"));

        Assert.False(store.CanSetUp);
        Assert.False(store.TrySetUp("someone", "another password"));
        var validator = new CredentialValidator(store);
        Assert.True(validator.IsValid("studio", "correct horse"));
        Assert.False(validator.IsValid("someone", "another password"));

        // After a restart, from the file.
        Assert.True(new CredentialValidator(Create(localOnly: true)).IsValid("studio", "correct horse"));
        Assert.DoesNotContain("correct horse", File.ReadAllText(Paths.LoginFile));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Paths.LoginFile));
        }
    }

    [Fact]
    public void Mac_mini_never_offers_setup()
    {
        var store = Create(localOnly: false);
        Assert.False(store.CanSetUp);
        Assert.False(store.TrySetUp("studio", "correct horse"));
        Assert.False(File.Exists(Paths.LoginFile));
    }

    [Fact]
    public void Configured_login_wins_over_setup()
    {
        var store = Create(localOnly: true, new AuthOptions { Username = "studio", PasswordHash = CredentialValidator.HashPassword("configured") });
        Assert.False(store.CanSetUp);
        Assert.Equal("studio", store.Current?.Username);
    }

    [Fact]
    public void Damaged_login_file_keeps_setup_closed()
    {
        Directory.CreateDirectory(Paths.KeysDirectory);
        File.WriteAllText(Paths.LoginFile, "{ half");
        var store = Create(localOnly: true);
        Assert.Null(store.Current);
        Assert.False(store.CanSetUp);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("192.168.1.20", false)]
    [InlineData("100.101.102.103", false)]
    public void Setup_is_only_offered_on_this_computer(string remote, bool expected)
    {
        Assert.Equal(expected, Create(localOnly: true).CanSetUpFrom(IPAddress.Parse(remote)));
        Assert.False(Create(localOnly: true).CanSetUpFrom(null));
    }
}
