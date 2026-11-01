using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lotse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTelc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Exam",
                table: "Profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TelcSimulations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TasksJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelcSimulations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelcSimulations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelcAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Part = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    Points = table.Column<double>(type: "REAL", nullable: false),
                    MaxPoints = table.Column<double>(type: "REAL", nullable: false),
                    Utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    SimulationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AnswersJson = table.Column<string>(type: "TEXT", nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: true),
                    RatingJson = table.Column<string>(type: "TEXT", nullable: true),
                    RatedByAi = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelcAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelcAttempts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TelcAttempts_TelcSimulations_SimulationId",
                        column: x => x.SimulationId,
                        principalTable: "TelcSimulations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelcAttempts_SimulationId",
                table: "TelcAttempts",
                column: "SimulationId");

            migrationBuilder.CreateIndex(
                name: "IX_TelcAttempts_UserId_Part",
                table: "TelcAttempts",
                columns: new[] { "UserId", "Part" });

            migrationBuilder.CreateIndex(
                name: "IX_TelcSimulations_UserId",
                table: "TelcSimulations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelcAttempts");

            migrationBuilder.DropTable(
                name: "TelcSimulations");

            migrationBuilder.DropColumn(
                name: "Exam",
                table: "Profiles");
        }
    }
}
