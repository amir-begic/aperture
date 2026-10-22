using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aperture.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class PackagesWithoutCalendarFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalendarFeedToken",
                table: "BusinessSettings");

            migrationBuilder.AddColumn<int>(
                name: "PackageId",
                table: "Shoots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Packages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ShootType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Price = table.Column<long>(type: "INTEGER", nullable: false),
                    Hours = table.Column<decimal>(type: "TEXT", nullable: false),
                    DeliveryDays = table.Column<int>(type: "INTEGER", nullable: true),
                    Included = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shoots_PackageId",
                table: "Shoots",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Packages_Name",
                table: "Packages",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_Shoots_Packages_PackageId",
                table: "Shoots",
                column: "PackageId",
                principalTable: "Packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shoots_Packages_PackageId",
                table: "Shoots");

            migrationBuilder.DropTable(
                name: "Packages");

            migrationBuilder.DropIndex(
                name: "IX_Shoots_PackageId",
                table: "Shoots");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "Shoots");

            migrationBuilder.AddColumn<string>(
                name: "CalendarFeedToken",
                table: "BusinessSettings",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "BusinessSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "CalendarFeedToken",
                value: null);
        }
    }
}
