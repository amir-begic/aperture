using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aperture.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GigId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SellerIban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: true),
                    SellerEmail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SellerPhone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SellerVatNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    VatRatePercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    AlreadyPaid = table.Column<long>(type: "INTEGER", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PdfId = table.Column<int>(type: "INTEGER", nullable: true),
                    Client_City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Client_Company = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Client_CountryCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Client_Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Client_PostalCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Client_Street = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Seller_City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Seller_Company = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Seller_CountryCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Seller_Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Seller_PostalCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Seller_Street = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Attachments_PdfId",
                        column: x => x.PdfId,
                        principalTable: "Attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Gigs_GigId",
                        column: x => x.GigId,
                        principalTable: "Gigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    UnitPrice = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_GigId",
                table: "Invoices",
                column: "GigId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Number",
                table: "Invoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PdfId",
                table: "Invoices",
                column: "PdfId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "Invoices");
        }
    }
}
