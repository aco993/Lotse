using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lotse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFsrsMemoryModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Difficulty",
                table: "ReviewStates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Stability",
                table: "ReviewStates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "ReviewStates");

            migrationBuilder.DropColumn(
                name: "Stability",
                table: "ReviewStates");
        }
    }
}
