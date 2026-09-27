using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClassifyInternalTransfersPerTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InternalTransferAccountId",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalTransferSource",
                table: "transactions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE transactions t
                SET "InternalTransferAccountId" = it."CreditAccountId", "InternalTransferSource" = 'manual'
                FROM internal_transfers it
                WHERE it."Status" = 'confirmed' AND it."DebitTransactionId" = t."Id"
                """);

            migrationBuilder.Sql("""
                UPDATE transactions t
                SET "InternalTransferAccountId" = it."DebitAccountId", "InternalTransferSource" = 'manual'
                FROM internal_transfers it
                WHERE it."Status" = 'confirmed' AND it."CreditTransactionId" = t."Id"
                """);

            migrationBuilder.Sql("""
                UPDATE tenants SET "FinancialDataVersion" = "FinancialDataVersion" + 1
                """);

            migrationBuilder.DropTable(
                name: "internal_transfers");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_InternalTransferAccountId",
                table: "transactions",
                column: "InternalTransferAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_TenantId_InternalTransferAccountId",
                table: "transactions",
                columns: new[] { "TenantId", "InternalTransferAccountId" });

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_accounts_InternalTransferAccountId",
                table: "transactions",
                column: "InternalTransferAccountId",
                principalTable: "accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_accounts_InternalTransferAccountId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_InternalTransferAccountId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_TenantId_InternalTransferAccountId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "InternalTransferAccountId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "InternalTransferSource",
                table: "transactions");

            migrationBuilder.CreateTable(
                name: "internal_transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebitTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreditAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditPostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DebitAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebitPostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_internal_transfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_internal_transfers_transactions_CreditTransactionId",
                        column: x => x.CreditTransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_internal_transfers_transactions_DebitTransactionId",
                        column: x => x.DebitTransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_internal_transfers_CreditTransactionId",
                table: "internal_transfers",
                column: "CreditTransactionId",
                unique: true,
                filter: "\"Status\" = 'confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_internal_transfers_DebitTransactionId",
                table: "internal_transfers",
                column: "DebitTransactionId",
                unique: true,
                filter: "\"Status\" = 'confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_internal_transfers_TenantId_DebitTransactionId_CreditTransa~",
                table: "internal_transfers",
                columns: new[] { "TenantId", "DebitTransactionId", "CreditTransactionId" },
                unique: true);
        }
    }
}
