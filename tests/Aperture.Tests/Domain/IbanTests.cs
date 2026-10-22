using Aperture.Web.Domain;

namespace Aperture.Tests.Domain;

public class IbanTests
{
    [Theory]
    [InlineData("CH9300762011623852957")]
    [InlineData("CH93 0076 2011 6238 5295 7")]
    [InlineData("ch93 0076 2011 6238 5295 7")]
    [InlineData("DE89370400440532013000")]
    public void Valid_ibans_pass(string iban)
    {
        Assert.True(Iban.IsValid(iban));
    }

    [Theory]
    [InlineData("")]
    [InlineData("CH9400762011623852957")]   // wrong check digits
    [InlineData("CH93007620116238529")]     // too short
    [InlineData("1293 0076 2011 6238 5295 7")]
    [InlineData("CH93-0076-2011-6238-5295-7")]
    public void Invalid_ibans_fail(string iban)
    {
        Assert.False(Iban.IsValid(iban));
    }

    [Fact]
    public void Normalize_removes_spaces_and_upper_cases()
    {
        Assert.Equal("CH9300762011623852957", Iban.Normalize(" ch93 0076 2011 6238 5295 7 "));
    }

    [Fact]
    public void Attribute_leaves_empty_values_to_required()
    {
        var attribute = new ValidIbanAttribute();

        Assert.True(attribute.IsValid(""));
        Assert.True(attribute.IsValid(null));
        Assert.False(attribute.IsValid("CH9400762011623852957"));
    }
}
