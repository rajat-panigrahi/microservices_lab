using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyOps.Projects.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialProjectsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "objectives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Horizon = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_objectives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sponsor = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Budget = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Health = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    HealthReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ActivatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_objectives_Code",
                table: "objectives",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_Id",
                table: "outbox_messages",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                table: "outbox_messages",
                columns: new[] { "ProcessedAtUtc", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_Code",
                table: "projects",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_projects_ObjectiveId",
                table: "projects",
                column: "ObjectiveId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "objectives");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "projects");
        }
    }
}
