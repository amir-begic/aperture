using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aperture.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BusinessName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OwnerName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Street = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PostalCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "INTEGER", nullable: false),
                    IsVatRegistered = table.Column<bool>(type: "INTEGER", nullable: false),
                    VatNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    VatRatePercent = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "BusinessSettings",
                columns: new[] { "Id", "BusinessName", "City", "CountryCode", "Email", "Iban", "IsVatRegistered", "OwnerName", "PaymentTermsDays", "Phone", "PostalCode", "Street", "VatNumber", "VatRatePercent", "Website" },
                values: new object[] { 1, "", "", "CH", null, "", false, "", 30, null, "", "", null, 8.1m, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessSettings");
        }
    }
}
