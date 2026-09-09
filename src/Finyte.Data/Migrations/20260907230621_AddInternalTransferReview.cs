using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalTransferReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "internal_transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebitTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DebitAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebitPostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreditPostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "internal_transfers");
        }
    }
}
