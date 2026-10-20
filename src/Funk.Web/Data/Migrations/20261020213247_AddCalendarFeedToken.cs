using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Funk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarFeedToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalendarFeedToken",
                table: "BusinessSettings");
        }
    }
}
