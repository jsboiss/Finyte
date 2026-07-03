using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_members_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.DropIndex(
                name: "IX_transactions_UserId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_UserId_FiskilTransactionId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_accounts_UserId",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "IX_accounts_UserId_FiskilAccountId",
                table: "accounts");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "transactions",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "accounts",
                type: "uuid",
                nullable: false);

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_TenantId",
                table: "transactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_TenantId_FiskilTransactionId",
                table: "transactions",
                columns: new[] { "TenantId", "FiskilTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_TenantId",
                table: "accounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_TenantId_FiskilAccountId",
                table: "accounts",
                columns: new[] { "TenantId", "FiskilAccountId" },
                unique: true,
                filter: "\"FiskilAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_members_TenantId",
                table: "tenant_members",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_members_UserId",
                table: "tenant_members",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_members");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropIndex(
                name: "IX_transactions_TenantId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_TenantId_FiskilTransactionId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_accounts_TenantId",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "IX_accounts_TenantId_FiskilAccountId",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "accounts");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "transactions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "accounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_UserId",
                table: "transactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_UserId_FiskilTransactionId",
                table: "transactions",
                columns: new[] { "UserId", "FiskilTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_UserId",
                table: "accounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_UserId_FiskilAccountId",
                table: "accounts",
                columns: new[] { "UserId", "FiskilAccountId" },
                unique: true,
                filter: "\"FiskilAccountId\" IS NOT NULL");
        }
    }
}
