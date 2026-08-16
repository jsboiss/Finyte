using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipSyncAndConnectionOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tenant_members_UserId",
                table: "tenant_members");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemovedAt",
                table: "tenant_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantMemberId",
                table: "provider_connections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderConnectionId",
                table: "accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE provider_connections AS connection
                SET "TenantMemberId" = (
                    SELECT member."Id"
                    FROM tenant_members AS member
                    WHERE member."TenantId" = connection."TenantId"
                    ORDER BY member."Role" = 'Owner' DESC, member."CreatedAt"
                    LIMIT 1
                );

                UPDATE accounts AS account
                SET "ProviderConnectionId" = connection."Id"
                FROM provider_connections AS connection
                WHERE account."TenantId" = connection."TenantId"
                  AND account."ConsentId" IS NOT NULL
                  AND account."ConsentId" = connection."ConsentId";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantMemberId",
                table: "provider_connections",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "clerk_webhook_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clerk_webhook_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_members_TenantId_UserId",
                table: "tenant_members",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_connections_TenantMemberId",
                table: "provider_connections",
                column: "TenantMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_ProviderConnectionId",
                table: "accounts",
                column: "ProviderConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_clerk_webhook_events_MessageId",
                table: "clerk_webhook_events",
                column: "MessageId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_accounts_provider_connections_ProviderConnectionId",
                table: "accounts",
                column: "ProviderConnectionId",
                principalTable: "provider_connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_provider_connections_tenant_members_TenantMemberId",
                table: "provider_connections",
                column: "TenantMemberId",
                principalTable: "tenant_members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accounts_provider_connections_ProviderConnectionId",
                table: "accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_provider_connections_tenant_members_TenantMemberId",
                table: "provider_connections");

            migrationBuilder.DropTable(
                name: "clerk_webhook_events");

            migrationBuilder.DropIndex(
                name: "IX_tenant_members_TenantId_UserId",
                table: "tenant_members");

            migrationBuilder.DropIndex(
                name: "IX_provider_connections_TenantMemberId",
                table: "provider_connections");

            migrationBuilder.DropIndex(
                name: "IX_accounts_ProviderConnectionId",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "tenant_members");

            migrationBuilder.DropColumn(
                name: "TenantMemberId",
                table: "provider_connections");

            migrationBuilder.DropColumn(
                name: "ProviderConnectionId",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_members_UserId",
                table: "tenant_members",
                column: "UserId",
                unique: true);
        }
    }
}
