using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lotse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadinessSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReadinessSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Overall = table.Column<double>(type: "REAL", nullable: false),
                    ModulesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadinessSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadinessSnapshots_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessSnapshots_UserId_Day",
                table: "ReadinessSnapshots",
                columns: new[] { "UserId", "Day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadinessSnapshots");
        }
    }
}
