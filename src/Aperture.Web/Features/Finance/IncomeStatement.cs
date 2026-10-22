using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Shoots;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Features.Finance;

public sealed record IncomeEntry(
    DateOnly Date, int ShootId, string ShootTitle, string ClientName, string? InvoiceNumber,
    PaymentMethod Method, string? Note, Money Amount, Attachment? Proof);

public sealed record ExpenseEntry(
    int Id, DateOnly Date, ExpenseCategory Category, string Description, string? Vendor,
    int? ShootId, string? ShootTitle, Money Amount, Attachment? Receipt);

public sealed record AssetEntry(Asset Asset, DepreciationYear Year);

/// <summary>A shoot that took place in the year: what it brings in against what it cost.</summary>
public sealed record ShootResult(int ShootId, string Title, DateTime Start, string ClientName, Money? ToBill, Money Costs, Money Received)
{
    public Money? Margin => ToBill - Costs;
}

public sealed record MonthTotals(int Month, Money Income, Money Costs);

/// <summary>
/// One year on a cash basis, as simplified bookkeeping for small self-employed businesses does it:
/// income is what was received, costs what was paid, plus depreciation of equipment.
/// </summary>
public sealed record IncomeStatement(
    int Year,
    IReadOnlyList<IncomeEntry> Income,
    IReadOnlyList<ExpenseEntry> Expenses,
    IReadOnlyList<AssetEntry> Assets,
    IReadOnlyList<ShootResult> Shoots,
    bool ProRataFirstYear = false)
{
    public Money TotalIncome => Income.Sum(i => i.Amount);
    public Money TotalCosts => Expenses.Sum(e => e.Amount);
    public Money TotalDepreciation => Assets.Sum(a => a.Year.Depreciation);

    /// <summary>Sale proceeds minus remaining book value of equipment sold this year.</summary>
    public Money DisposalResult => Assets.Sum(a => a.Year.DisposalResult ?? Money.Zero);

    public Money Result => TotalIncome - TotalCosts - TotalDepreciation + DisposalResult;

    /// <summary>Non-empty categories in their usual order.</summary>
    public IReadOnlyList<(ExpenseCategory Category, Money Amount)> CostsByCategory =>
        ExpenseCategories.All
            .Select(c => (c.Value, Expenses.Where(e => e.Category == c.Value).Sum(e => e.Amount)))
            .Where(c => c.Item2 != Money.Zero)
            .ToList();

    public IReadOnlyList<MonthTotals> Months =>
        Enumerable.Range(1, 12)
            .Select(month => new MonthTotals(
                month,
                Income.Where(i => i.Date.Month == month).Sum(i => i.Amount),
                Expenses.Where(e => e.Date.Month == month).Sum(e => e.Amount)))
            .ToList();

    public static async Task<IncomeStatement> LoadAsync(ApertureDbContext db, int year)
    {
        var from = new DateOnly(year, 1, 1);
        var until = new DateOnly(year + 1, 1, 1);

        var payments = await db.Payments.AsNoTracking()
            .Include(p => p.Proof)
            .Include(p => p.Shoot).ThenInclude(g => g.Client)
            .Where(p => p.Date >= from && p.Date < until)
            .OrderBy(p => p.Date).ThenBy(p => p.Id)
            .ToListAsync();
        var invoiceNumbers = await db.Invoices
            .Where(i => i.Status == InvoiceStatus.Issued)
            .Select(i => new { i.ShootId, i.Number })
            .ToDictionaryAsync(i => i.ShootId, i => i.Number);
        var income = payments.Select(p => new IncomeEntry(
                p.Date, p.ShootId, p.Shoot.Title, p.Shoot.Client.Name, invoiceNumbers.GetValueOrDefault(p.ShootId),
                p.Method, p.Note, p.Amount, p.Proof))
            .ToList();

        var expenses = (await db.Expenses.AsNoTracking()
                .Include(e => e.Receipt)
                .Include(e => e.Shoot)
                .Where(e => e.Date >= from && e.Date < until)
                .OrderBy(e => e.Date).ThenBy(e => e.Id)
                .ToListAsync())
            .Select(e => new ExpenseEntry(e.Id, e.Date, e.Category, e.Description, e.Vendor, e.ShootId, e.Shoot?.Title, e.Amount, e.Receipt))
            .ToList();

        var proRataFirstYear = await db.BusinessSettings.Select(s => s.ProRataFirstYear).SingleAsync();
        var assets = (await db.Assets.AsNoTracking()
                .Include(a => a.Receipt)
                .Where(a => a.PurchaseDate < until && (a.DisposedOn == null || a.DisposedOn >= from))
                .OrderBy(a => a.PurchaseDate)
                .ToListAsync())
            .Select(a => (Asset: a, Year: a.Schedule(year, proRataFirstYear).LastOrDefault(y => y.Year == year)))
            .Where(a => a.Year is not null)
            .Select(a => new AssetEntry(a.Asset, a.Year!))
            .ToList();

        var shootRows = await db.Shoots
            .Where(g => g.Start >= from.ToDateTime(TimeOnly.MinValue) && g.Start < until.ToDateTime(TimeOnly.MinValue)
                && g.Status != ShootStatus.Cancelled)
            .OrderBy(g => g.Start)
            .ToRowsAsync(db);
        var shootIds = shootRows.Select(r => r.Id).ToList();
        var shootCosts = (await db.Expenses.Where(e => e.ShootId != null && shootIds.Contains(e.ShootId.Value))
                .Select(e => new { ShootId = e.ShootId!.Value, e.Amount }).ToListAsync())
            .GroupBy(e => e.ShootId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        var shoots = shootRows
            .Select(r => new ShootResult(r.Id, r.Title, r.Start, r.ClientName, r.Financials.Total, shootCosts.GetValueOrDefault(r.Id), r.Received))
            .ToList();

        return new IncomeStatement(year, income, expenses, assets, shoots, proRataFirstYear);
    }

    /// <summary>Years with any data, newest first, always including the current one.</summary>
    public static async Task<List<int>> YearsAsync(ApertureDbContext db, int currentYear)
    {
        var years = new HashSet<int> { currentYear };
        years.UnionWith(await db.Payments.Select(p => p.Date.Year).Distinct().ToListAsync());
        years.UnionWith(await db.Expenses.Select(e => e.Date.Year).Distinct().ToListAsync());
        years.UnionWith(await db.Assets.Select(a => a.PurchaseDate.Year).Distinct().ToListAsync());
        years.UnionWith(await db.Shoots.Select(g => g.Start.Year).Distinct().ToListAsync());
        return years.OrderDescending().ToList();
    }
}
