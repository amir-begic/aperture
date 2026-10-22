using System.Net;
using Aperture.Web.Auth;
using Aperture.Web.Configuration;

namespace Aperture.Tests.Auth;

public class LoginStoreTests : IDisposable
{
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-login-").FullName;

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    private AperturePaths Paths => new(_dataDirectory);

    private LoginStore Create() => new(Paths);

    [Fact]
    public void Creates_the_login_once_and_keeps_it()
    {
        var store = Create();
        Assert.True(store.CanSetUp);

        Assert.True(store.TrySetUp(" studio ", "correct horse"));

        Assert.False(store.CanSetUp);
        Assert.False(store.TrySetUp("someone", "another password"));
        var validator = new CredentialValidator(store);
        Assert.True(validator.IsValid("studio", "correct horse"));
        Assert.False(validator.IsValid("someone", "another password"));

        // After a restart, from the file.
        Assert.True(new CredentialValidator(Create()).IsValid("studio", "correct horse"));
        Assert.DoesNotContain("correct horse", File.ReadAllText(Paths.LoginFile));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Paths.LoginFile));
        }
    }

    [Fact]
    public void Damaged_login_file_keeps_setup_closed()
    {
        Directory.CreateDirectory(Paths.KeysDirectory);
        File.WriteAllText(Paths.LoginFile, "{ half");
        var store = Create();
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
        Assert.Equal(expected, Create().CanSetUpFrom(IPAddress.Parse(remote)));
        Assert.False(Create().CanSetUpFrom(null));
    }
}
