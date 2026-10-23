using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Aperture.Web.Localization;
using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;

namespace Aperture.Web.Components;

/// <summary>
/// Checks a form field against the data annotations on its model property, like RadzenDataAnnotationValidator, but
/// with messages in the page's language. An attribute's own ErrorMessage is a text key; the built-in ones without a
/// message get a short one here instead of .NET's "The QuotedPriceChf field is required.", which is English only and
/// names the property.
/// </summary>
public sealed class FieldValidator : ValidatorBase
{
    [Inject]
    private IServiceProvider Services { get; set; } = default!;

    /// <summary>The messages of the failed checks; set by <see cref="Validate"/>.</summary>
    [Parameter]
    public override string Text { get; set; } = "";

    protected override bool Validate(IRadzenFormComponent component)
    {
        var field = component.FieldIdentifier;
        if (field.Model.GetType().GetProperty(field.FieldName) is not { } property)
        {
            return true;
        }

        var value = component.GetValue();
        var context = new ValidationContext(field.Model, Services, items: null) { MemberName = field.FieldName };
        var messages = property.GetCustomAttributes<ValidationAttribute>()
            .Where(attribute => attribute.GetValidationResult(value, context) != ValidationResult.Success)
            .Select(Message)
            .ToList();

        Text = string.Join(" ", messages);
        return messages.Count == 0;
    }

    private static string Message(ValidationAttribute attribute) => attribute switch
    {
        { ErrorMessage: { } key } => Texts.Get(key),
        RequiredAttribute => Texts.Get("Required."),
        MaxLengthAttribute maxLength => Texts.Format("At most {0} characters.", maxLength.Length),
        RangeAttribute range => Texts.Format("Between {0} and {1}.", range.Minimum, range.Maximum),
        _ => Texts.Get(attribute.FormatErrorMessage("")),
    };
}
