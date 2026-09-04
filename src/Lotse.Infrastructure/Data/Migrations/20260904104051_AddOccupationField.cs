using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lotse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOccupationField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "Occupation" changes meaning here: it was free text for the tutor prompt and becomes the structured
            // field that steers the planner. Scaffolding alone would have dropped the text (EF says so: "may result
            // in the loss of data"), so it is carried into its own column first, and the old values are cleared
            // before the type change - SQLite would happily keep "Softwareentwickler" in an INTEGER column and only
            // fail when something reads it as an enum.
            migrationBuilder.AddColumn<string>(
                name: "JobTitle",
                table: "Profiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE Profiles SET JobTitle = Occupation;");
            migrationBuilder.Sql("UPDATE Profiles SET Occupation = '0';"); // 0 = Occupation.Unspecified

            migrationBuilder.AlterColumn<int>(
                name: "Occupation",
                table: "Profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Mirror image: widen the column back to text, move the job title home, then drop the extra column.
            migrationBuilder.AlterColumn<string>(
                name: "Occupation",
                table: "Profiles",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.Sql("UPDATE Profiles SET Occupation = JobTitle;");

            migrationBuilder.DropColumn(
                name: "JobTitle",
                table: "Profiles");
        }
    }
}
