using Funk.Web.Auth;
using Microsoft.Extensions.Options;

namespace Funk.Tests.Auth;

public class CredentialValidatorTests
{
    private readonly CredentialValidator _validator = new(Options.Create(new AuthOptions
    {
        Username = "dj",
        PasswordHash = CredentialValidator.HashPassword("correct horse"),
    }));

    [Theory]
    [InlineData("dj")]
    [InlineData("DJ")]
    [InlineData(" dj ")]
    public void Accepts_correct_credentials(string username)
    {
        Assert.True(_validator.IsValid(username, "correct horse"));
    }

    [Theory]
    [InlineData("dj", "wrong")]
    [InlineData("dj", "Correct horse")]
    [InlineData("dj", "")]
    [InlineData("dj", null)]
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
