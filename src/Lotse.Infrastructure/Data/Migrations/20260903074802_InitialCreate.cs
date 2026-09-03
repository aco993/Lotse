using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lotse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExerciseId = table.Column<string>(type: "TEXT", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    HintUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AnswerText = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ErrorEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AttemptId = table.Column<long>(type: "INTEGER", nullable: true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", nullable: false),
                    Utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Snippet = table.Column<string>(type: "TEXT", nullable: true),
                    Correction = table.Column<string>(type: "TEXT", nullable: true),
                    FromAi = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedExercises",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedExercises", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Productions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExerciseId = table.Column<string>(type: "TEXT", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", nullable: false),
                    IsSpeaking = table.Column<bool>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: true),
                    EstimatedLevel = table.Column<string>(type: "TEXT", nullable: true),
                    EvaluationJson = table.Column<string>(type: "TEXT", nullable: true),
                    EvaluatedByAi = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Productions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NativeLanguage = table.Column<string>(type: "TEXT", nullable: false),
                    Occupation = table.Column<string>(type: "TEXT", nullable: true),
                    DailyMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetExamDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlacementCompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReviewStates",
                columns: table => new
                {
                    ExerciseId = table.Column<string>(type: "TEXT", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", nullable: false),
                    IntervalDays = table.Column<double>(type: "REAL", nullable: false),
                    Ease = table.Column<double>(type: "REAL", nullable: false),
                    Repetitions = table.Column<int>(type: "INTEGER", nullable: false),
                    Lapses = table.Column<int>(type: "INTEGER", nullable: false),
                    DueUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastReviewUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewStates", x => x.ExerciseId);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlanJson = table.Column<string>(type: "TEXT", nullable: false),
                    StepsTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    StepsDone = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SkillStates",
                columns: table => new
                {
                    NodeId = table.Column<string>(type: "TEXT", nullable: false),
                    Theta = table.Column<double>(type: "REAL", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    Correct = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStreak = table.Column<int>(type: "INTEGER", nullable: false),
                    LastPracticedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WasWeak = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecheckDueUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RechecksPassed = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillStates", x => x.NodeId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_ExerciseId",
                table: "Attempts",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_Utc",
                table: "Attempts",
                column: "Utc");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorEvents_NodeId",
                table: "ErrorEvents",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorEvents_Utc",
                table: "ErrorEvents",
                column: "Utc");

            migrationBuilder.CreateIndex(
                name: "IX_Productions_Utc",
                table: "Productions",
                column: "Utc");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewStates_DueUtc",
                table: "ReviewStates",
                column: "DueUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_StartedUtc",
                table: "Sessions",
                column: "StartedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attempts");

            migrationBuilder.DropTable(
                name: "ErrorEvents");

            migrationBuilder.DropTable(
                name: "GeneratedExercises");

            migrationBuilder.DropTable(
                name: "Productions");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "ReviewStates");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "SkillStates");
        }
    }
}
