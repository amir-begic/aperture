using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aperture.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceLanguageOnTheInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An invoice's language is chosen on its draft (Invoices.Language), starting from Aperture's own.
            migrationBuilder.DropColumn(
                name: "InvoiceLanguage",
                table: "Clients");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceLanguage",
                table: "Clients",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "English");
        }
    }
}
