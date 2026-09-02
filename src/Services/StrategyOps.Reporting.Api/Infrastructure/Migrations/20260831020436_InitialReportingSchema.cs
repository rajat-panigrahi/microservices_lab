using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyOps.Reporting.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialReportingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Consumer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProcessedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => new { x.MessageId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "portfolio_scorecards",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectCode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ProjectName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Health = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    HealthReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Budget = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    KpiTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    KpiGreen = table.Column<int>(type: "INTEGER", nullable: false),
                    KpiAmber = table.Column<int>(type: "INTEGER", nullable: false),
                    KpiRed = table.Column<int>(type: "INTEGER", nullable: false),
                    KpiNotMeasured = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenRisks = table.Column<int>(type: "INTEGER", nullable: false),
                    CriticalOpenRisks = table.Column<int>(type: "INTEGER", nullable: false),
                    EscalatedRisks = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenIssues = table.Column<int>(type: "INTEGER", nullable: false),
                    CriticalOpenIssues = table.Column<int>(type: "INTEGER", nullable: false),
                    BenefitForecast = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BenefitRealised = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RealisationPercent = table.Column<decimal>(type: "TEXT", precision: 9, scale: 2, nullable: false),
                    BenefitStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastUpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolio_scorecards", x => x.ProjectId);
                });

            migrationBuilder.CreateTable(
                name: "project_kpi_statuses",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KpiId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Rag = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_kpi_statuses", x => new { x.ProjectId, x.KpiId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_scorecards_ProjectCode",
                table: "portfolio_scorecards",
                column: "ProjectCode");

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_scorecards_Stage",
                table: "portfolio_scorecards",
                column: "Stage");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "portfolio_scorecards");

            migrationBuilder.DropTable(
                name: "project_kpi_statuses");
        }
    }
}
