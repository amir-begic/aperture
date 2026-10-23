using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aperture.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Quotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShootId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    QuoteDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Included = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AnsweredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
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
                    Seller_Street = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SellerEmail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SellerPhone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SellerVatNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    VatRatePercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quotes_Attachments_PdfId",
                        column: x => x.PdfId,
                        principalTable: "Attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Quotes_Shoots_ShootId",
                        column: x => x.ShootId,
                        principalTable: "Shoots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QuoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    UnitPrice = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteLines_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLines_QuoteId",
                table: "QuoteLines",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_Number",
                table: "Quotes",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_PdfId",
                table: "Quotes",
                column: "PdfId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_ShootId",
                table: "Quotes",
                column: "ShootId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteLines");

            migrationBuilder.DropTable(
                name: "Quotes");
        }
    }
}
