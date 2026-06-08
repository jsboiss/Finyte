using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderSyncFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EndUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ConsentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InstitutionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "provider_sync_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Dataset = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EndUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExternalMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ChangeSummaryJson = table.Column<string>(type: "jsonb", nullable: true),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProjectionRefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_sync_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "provider_webhook_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SyncRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_webhook_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provider_connections_Provider_EndUserId",
                table: "provider_connections",
                columns: new[] { "Provider", "EndUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_connections_TenantId",
                table: "provider_connections",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_sync_runs_ExternalMessageId",
                table: "provider_sync_runs",
                column: "ExternalMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_sync_runs_TenantId",
                table: "provider_sync_runs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_sync_runs_TenantId_Status",
                table: "provider_sync_runs",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_provider_webhook_events_EventType",
                table: "provider_webhook_events",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_provider_webhook_events_Provider_MessageId",
                table: "provider_webhook_events",
                columns: new[] { "Provider", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_webhook_events_SyncRunId",
                table: "provider_webhook_events",
                column: "SyncRunId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_webhook_events_TenantId",
                table: "provider_webhook_events",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_connections");

            migrationBuilder.DropTable(
                name: "provider_sync_runs");

            migrationBuilder.DropTable(
                name: "provider_webhook_events");
        }
    }
}
