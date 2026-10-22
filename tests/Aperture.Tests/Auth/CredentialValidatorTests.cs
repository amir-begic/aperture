using Aperture.Web.Auth;
using Microsoft.Extensions.Options;

namespace Aperture.Tests.Auth;

public class CredentialValidatorTests
{
    private readonly CredentialValidator _validator = new(Options.Create(new AuthOptions
    {
        Username = "studio",
        PasswordHash = CredentialValidator.HashPassword("correct horse"),
    }));

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
