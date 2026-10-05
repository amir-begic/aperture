using System.ComponentModel.DataAnnotations;

namespace Funk.Web.Domain;

// The built-in [EmailAddress] and [Phone] accept null but reject "", which is what an input
// sends after you type in it and clear it again. These treat blank as "not provided".

[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionalEmailAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute Email = new();

    public OptionalEmailAttribute() : base("Not a valid email address.") { }

    public override bool IsValid(object? value) =>
        value is not string s || string.IsNullOrWhiteSpace(s) || Email.IsValid(s.Trim());
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionalPhoneAttribute : ValidationAttribute
{
    private static readonly PhoneAttribute Phone = new();

    public OptionalPhoneAttribute() : base("Not a valid phone number.") { }

    public override bool IsValid(object? value) =>
        value is not string s || string.IsNullOrWhiteSpace(s) || Phone.IsValid(s.Trim());
}
