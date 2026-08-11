using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NileJewal.Migrations
{
    /// <inheritdoc />
    public partial class AddMealTypeToBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MealType",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MealType",
                table: "Bookings");
        }
    }
}
