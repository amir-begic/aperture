using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Finance;

namespace Aperture.Tests.Features;

public sealed class IncomeStatementTests : IAsyncLifetime
{
    private readonly TestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    private static Money Chf(decimal value) => Money.FromChf(value);

    private async Task SeedAsync()
    {
        await using var db = _database.CreateContext();
        var client = new Client { Name = "Anna & Marco Rossi" };
        var wedding = new Shoot
        {
            Title = "Wedding Rossi", Client = client, Status = ShootStatus.Done, QuotedPrice = Chf(1800),
            Start = new DateTime(2026, 6, 13, 20, 0, 0), End = new DateTime(2026, 6, 14, 2, 0, 0),
        };
        wedding.Expenses.Add(new Expense { Date = new DateOnly(2026, 6, 13), Category = ExpenseCategory.GearRental,
            Description = "Lights", Amount = Chf(350), BilledToClient = Chf(450) });
        wedding.Payments.Add(new Payment { Date = new DateOnly(2025, 12, 20), Amount = Chf(500), Note = "Deposit" }); // previous year
        wedding.Payments.Add(new Payment { Date = new DateOnly(2026, 6, 20), Amount = Chf(1750) });
        db.Shoots.Add(wedding);

        db.Shoots.Add(new Shoot
        {
            Title = "Cancelled party", Client = client, Status = ShootStatus.Cancelled,
            Start = new DateTime(2026, 7, 1, 20, 0, 0), End = new DateTime(2026, 7, 1, 23, 0, 0),
        });

        db.Expenses.Add(new Expense { Date = new DateOnly(2026, 1, 5), Category = ExpenseCategory.Software,
            Description = "Rekordbox subscription", Amount = Chf(120) });
        db.Expenses.Add(new Expense { Date = new DateOnly(2026, 3, 1), Category = ExpenseCategory.Insurance,
            Description = "Equipment insurance", Amount = Chf(230) });
        db.Expenses.Add(new Expense { Date = new DateOnly(2025, 12, 31), Category = ExpenseCategory.Software,
            Description = "Previous year", Amount = Chf(999) });

        db.Assets.Add(new Asset { Name = "Controller", Category = AssetCategory.AudioEquipment,
            PurchaseDate = new DateOnly(2026, 1, 10), Cost = Chf(2000), DepreciationRatePercent = 30 });
        db.Assets.Add(new Asset { Name = "Old speakers", Category = AssetCategory.AudioEquipment,
            PurchaseDate = new DateOnly(2024, 1, 1), Cost = Chf(1000), DepreciationRatePercent = 30,
            DisposedOn = new DateOnly(2026, 7, 1), SaleProceeds = Chf(500) });
        db.Assets.Add(new Asset { Name = "Sold long ago", Category = AssetCategory.Lighting,
            PurchaseDate = new DateOnly(2020, 1, 1), Cost = Chf(800), DisposedOn = new DateOnly(2023, 1, 1), SaleProceeds = Chf(100) });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Cash_basis_statement_for_one_year()
    {
        await SeedAsync();
        await using var db = _database.CreateContext();

        var statement = await IncomeStatement.LoadAsync(db, 2026);

        // Income: only the payment received in 2026 (the deposit belongs to 2025).
        Assert.Equal(Chf(1750), statement.TotalIncome);
        // Costs: lights 350 + software 120 + insurance 230.
        Assert.Equal(Chf(700), statement.TotalCosts);
        Assert.Equal(
            [(ExpenseCategory.GearRental, Chf(350)), (ExpenseCategory.Software, Chf(120)), (ExpenseCategory.Insurance, Chf(230))],
            statement.CostsByCategory);

        // Depreciation: controller 2000 * 30% = 600; old speakers: book value 490 at start of 2026
        // (1000 -> 700 -> 490), 6 months (Jan–Jun) = 73.50, then sold for 500 vs 416.50 left = +83.50.
        Assert.Equal(["Old speakers", "Controller"], statement.Assets.Select(a => a.Asset.Name)); // by purchase date
        Assert.Equal(Chf(673.50m), statement.TotalDepreciation);
        Assert.Equal(Chf(83.50m), statement.DisposalResult);
        Assert.Equal(Chf(1750 - 700 - 673.50m + 83.50m), statement.Result);

        var june = statement.Months.Single(m => m.Month == 6);
        Assert.Equal(Chf(1750), june.Income);
        Assert.Equal(Chf(350), june.Costs);
    }

    [Fact]
    public async Task Shoot_results_show_played_shoots_with_billed_costs_and_all_payments()
    {
        await SeedAsync();
        await using var db = _database.CreateContext();

        var shoot = (await IncomeStatement.LoadAsync(db, 2026)).Shoots.Single(); // cancelled shoot left out

        Assert.Equal("Wedding Rossi", shoot.Title);
        Assert.Equal(Chf(2250), shoot.ToBill);    // 1800 quote + 450 billed lights
        Assert.Equal(Chf(350), shoot.Costs);
        Assert.Equal(Chf(1900), shoot.Margin);
        Assert.Equal(Chf(2250), shoot.Received);  // incl. last year's deposit
    }

    [Fact]
    public async Task Years_with_data()
    {
        await SeedAsync();
        await using var db = _database.CreateContext();

        Assert.Equal([2026, 2025, 2024, 2020], await IncomeStatement.YearsAsync(db, 2026));
    }
}
