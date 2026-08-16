using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceQuartzAndVersionDashboardSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "projection_states");

            migrationBuilder.AddColumn<long>(
                name: "FinancialDataVersion",
                table: "tenants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "provider_sync_runs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("UPDATE provider_sync_runs SET \"BatchId\" = \"Id\" WHERE \"BatchId\" = '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchedAt",
                table: "provider_sync_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemporalWorkflowId",
                table: "provider_sync_runs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchedAt",
                table: "overview_projections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Generation",
                table: "overview_projections",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvalidatedAt",
                table: "overview_projections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "overview_projections",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "overview_projections",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "SourceVersion",
                table: "overview_projections",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "overview_projections",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "succeeded");

            migrationBuilder.AddColumn<string>(
                name: "TemporalWorkflowId",
                table: "overview_projections",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_TenantId_AccountId_PostedAt",
                table: "transactions",
                columns: new[] { "TenantId", "AccountId", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_TenantId_PostedAt",
                table: "transactions",
                columns: new[] { "TenantId", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_provider_sync_runs_BatchId",
                table: "provider_sync_runs",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_sync_runs_Status_DispatchedAt",
                table: "provider_sync_runs",
                columns: new[] { "Status", "DispatchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_overview_projections_Status_DispatchedAt",
                table: "overview_projections",
                columns: new[] { "Status", "DispatchedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transactions_TenantId_AccountId_PostedAt",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_TenantId_PostedAt",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_provider_sync_runs_BatchId",
                table: "provider_sync_runs");

            migrationBuilder.DropIndex(
                name: "IX_provider_sync_runs_Status_DispatchedAt",
                table: "provider_sync_runs");

            migrationBuilder.DropIndex(
                name: "IX_overview_projections_Status_DispatchedAt",
                table: "overview_projections");

            migrationBuilder.DropColumn(
                name: "FinancialDataVersion",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "provider_sync_runs");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "provider_sync_runs");

            migrationBuilder.DropColumn(
                name: "TemporalWorkflowId",
                table: "provider_sync_runs");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "overview_projections");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "overview_projections");

            migrationBuilder.DropColumn(
                name: "InvalidatedAt",
                table: "overview_projections");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "overview_projections");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "overview_projections");

            migrationBuilder.DropColumn(
                name: "SourceVersion",
                table: "overview_projections");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "overview_projections");

            migrationBuilder.DropColumn(
                name: "TemporalWorkflowId",
                table: "overview_projections");

            migrationBuilder.CreateTable(
                name: "projection_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsStale = table.Column<bool>(type: "boolean", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LastFailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSucceededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProjectionKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ScopeJson = table.Column<string>(type: "jsonb", nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StaleAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StaleReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projection_states", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_projection_states_TenantId_ProjectionKey_ScopeKey",
                table: "projection_states",
                columns: new[] { "TenantId", "ProjectionKey", "ScopeKey" },
                unique: true);
        }
    }
}
