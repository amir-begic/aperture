using System.ComponentModel.DataAnnotations;
using Aperture.Web.Data.Entities;

namespace Aperture.Tests.Domain;

public class ContactValidationTests
{
    [Theory]
    [InlineData("+41 79 123 45 67", null)]
    [InlineData(null, "anna@example.ch")]
    [InlineData("+41 79 123 45 67", "")]   // email typed, then cleared
    [InlineData("", "anna@example.ch")]    // phone typed, then cleared
    [InlineData("  ", "  ")]
    [InlineData(null, null)]
    public void Phone_and_email_are_optional(string? phone, string? email)
    {
        Assert.Empty(Validate(new Client { Name = "Anna", Phone = phone, Email = email }));
        Assert.Empty(Validate(ValidSettings(phone, email)));
    }

    [Theory]
    [InlineData("not a phone", null, nameof(Client.Phone))]
    [InlineData(null, "not-an-email", nameof(Client.Email))]
    public void Invalid_values_are_still_rejected(string? phone, string? email, string member)
    {
        var errors = Validate(new Client { Name = "Anna", Phone = phone, Email = email });

        Assert.Contains(errors, e => e.MemberNames.Contains(member));
    }

    private static BusinessSettings ValidSettings(string? phone, string? email) => new()
    {
        OwnerName = "Max Muster",
        Street = "Musterstrasse 1",
        PostalCode = "8000",
        City = "Zürich",
        Iban = "CH9300762011623852957",
        Phone = phone,
        Email = email,
    };

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
