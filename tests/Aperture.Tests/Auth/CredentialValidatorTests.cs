using Aperture.Web.Auth;
using Aperture.Web.Configuration;

namespace Aperture.Tests.Auth;

public class CredentialValidatorTests : IDisposable
{
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-credentials-").FullName;
    private readonly CredentialValidator _validator;

    public CredentialValidatorTests()
    {
        var logins = new LoginStore(new AperturePaths(_dataDirectory));
        logins.TrySetUp("studio", "correct horse");
        _validator = new CredentialValidator(logins);
    }

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    [Fact]
    public void Nobody_can_sign_in_before_the_login_is_created()
    {
        var empty = Directory.CreateTempSubdirectory("aperture-credentials-").FullName;
        try
        {
            Assert.False(new CredentialValidator(new LoginStore(new AperturePaths(empty))).IsValid("studio", "correct horse"));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Theory]
    [InlineData("studio")]
    [InlineData("STUDIO")]
    [InlineData(" studio ")]
    public void Accepts_correct_credentials(string username)
    {
        Assert.True(_validator.IsValid(username, "correct horse"));
    }

    [Theory]
    [InlineData("studio", "wrong")]
    [InlineData("studio", "Correct horse")]
    [InlineData("studio", "")]
    [InlineData("studio", null)]
    [InlineData("someone", "correct horse")]
    [InlineData(null, "correct horse")]
    public void Rejects_wrong_credentials(string? username, string? password)
    {
        Assert.False(_validator.IsValid(username, password));
    }

    [Fact]
    public void Hashes_are_salted()
    {
        Assert.NotEqual(CredentialValidator.HashPassword("same"), CredentialValidator.HashPassword("same"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("not base64!", false)]
    [InlineData("AQIDBA==", true)]
    public void IsWellFormedHash_checks_base64(string hash, bool expected)
    {
        Assert.Equal(expected, CredentialValidator.IsWellFormedHash(hash));
    }
}
