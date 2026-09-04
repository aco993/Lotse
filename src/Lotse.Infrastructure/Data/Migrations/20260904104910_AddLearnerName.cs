using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lotse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLearnerName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scaffolding guessed "Name" -> "LastName" (alphabetical luck, not meaning). The old field was labelled
            // "Name (für Musterlösungen)" and holds a first name, so it becomes FirstName and the surname is new.
            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Profiles",
                newName: "FirstName");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Profiles");

            migrationBuilder.RenameColumn(
                name: "FirstName",
                table: "Profiles",
                newName: "Name");
        }
    }
}
