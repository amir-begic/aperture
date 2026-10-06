using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Funk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProRataFirstYearSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ProRataFirstYear",
                table: "BusinessSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "BusinessSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "ProRataFirstYear",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProRataFirstYear",
                table: "BusinessSettings");
        }
    }
}
