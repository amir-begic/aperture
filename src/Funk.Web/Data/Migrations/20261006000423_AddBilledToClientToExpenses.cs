using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Funk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBilledToClientToExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BilledToClient",
                table: "Expenses",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BilledToClient",
                table: "Expenses");
        }
    }
}
