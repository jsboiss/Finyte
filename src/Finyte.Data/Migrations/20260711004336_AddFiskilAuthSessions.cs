using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFiskilAuthSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_provider_connections_Provider_EndUserId",
                table: "provider_connections");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "provider_connections",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE provider_connections
                SET "Status" = CASE
                    WHEN "ConsentId" IS NULL THEN 'pending'
                    ELSE 'active'
                END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "provider_connections",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "provider_auth_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EndUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_auth_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provider_auth_sessions_tenant_members_TenantMemberId",
                        column: x => x.TenantMemberId,
                        principalTable: "tenant_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provider_connections_Provider_ConsentId",
                table: "provider_connections",
                columns: new[] { "Provider", "ConsentId" },
                unique: true,
                filter: "\"ConsentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_provider_connections_Provider_EndUserId",
                table: "provider_connections",
                columns: new[] { "Provider", "EndUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_provider_auth_sessions_Provider_SessionId",
                table: "provider_auth_sessions",
                columns: new[] { "Provider", "SessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_auth_sessions_TenantId",
                table: "provider_auth_sessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_auth_sessions_TenantMemberId",
                table: "provider_auth_sessions",
                column: "TenantMemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_auth_sessions");

            migrationBuilder.DropIndex(
                name: "IX_provider_connections_Provider_ConsentId",
                table: "provider_connections");

            migrationBuilder.DropIndex(
                name: "IX_provider_connections_Provider_EndUserId",
                table: "provider_connections");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "provider_connections");

            migrationBuilder.CreateIndex(
                name: "IX_provider_connections_Provider_EndUserId",
                table: "provider_connections",
                columns: new[] { "Provider", "EndUserId" },
                unique: true);
        }
    }
}
