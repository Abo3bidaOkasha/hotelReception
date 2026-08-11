using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NileJewal.Migrations
{
    /// <inheritdoc />
    public partial class AddFoodOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DailyFoodAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "HasFood",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalFoodAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyFoodAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "HasFood",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TotalFoodAmount",
                table: "Bookings");
        }
    }
}
