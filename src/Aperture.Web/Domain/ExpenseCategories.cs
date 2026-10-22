using Aperture.Web.Data.Entities;

namespace Aperture.Web.Domain;

/// <summary>Display names and order of cost categories, roughly following a Swiss income statement.</summary>
public static class ExpenseCategories
{
    public static readonly IReadOnlyList<(ExpenseCategory Value, string Text)> All =
    [
        (ExpenseCategory.GearRental, "Gear & studio rental"),
        (ExpenseCategory.Travel, "Travel & transport"),
        (ExpenseCategory.Staff, "Second shooters & assistants"),
        (ExpenseCategory.Prints, "Prints, albums & lab"),
        (ExpenseCategory.SmallEquipment, "Small equipment & repairs"),
        (ExpenseCategory.Software, "Software & subscriptions"),
        (ExpenseCategory.Marketing, "Marketing & website"),
        (ExpenseCategory.Insurance, "Insurance & fees"),
        (ExpenseCategory.Phone, "Phone & internet"),
        (ExpenseCategory.Training, "Training"),
        (ExpenseCategory.Other, "Other"),
    ];

    public static string Text(this ExpenseCategory category) => All.First(c => c.Value == category).Text;
}
