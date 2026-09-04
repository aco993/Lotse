using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lotse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyGoal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WeeklyGoalMinutes",
                table: "Profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeeklyGoalMinutes",
                table: "Profiles");
        }
    }
}
