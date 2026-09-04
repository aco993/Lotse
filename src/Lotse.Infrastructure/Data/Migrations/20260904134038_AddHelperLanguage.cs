using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lotse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHelperLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HelperLanguage",
                table: "Profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HelperLanguage",
                table: "Profiles");
        }
    }
}
