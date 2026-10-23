using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aperture.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class LanguageForAppAndInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clients and invoices from before: English, as invoices were. The app language stays unset: pages
            // follow the browser until it's chosen in Settings.
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Invoices",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "English");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceLanguage",
                table: "Clients",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "English");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "BusinessSettings",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "BusinessSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "Language",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceLanguage",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "BusinessSettings");
        }
    }
}
