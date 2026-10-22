namespace Aperture.Web.Data.Entities;

/// <summary>Stored as text, so new categories need no migration. Display names: <see cref="Domain.ExpenseCategories"/>.</summary>
public enum ExpenseCategory
{
    GearRental,
    Travel,
    Staff,
    Prints,
    SmallEquipment,
    Software,
    Marketing,
    Insurance,
    Phone,
    Training,
    Other,
}
