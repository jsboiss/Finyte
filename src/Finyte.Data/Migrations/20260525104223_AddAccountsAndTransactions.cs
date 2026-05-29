using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountsAndTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FiskilAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AccountNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Bsb = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProductCategory = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InstitutionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ConsentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsOwned = table.Column<bool>(type: "boolean", nullable: true),
                    OpenStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AvailableBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreditLimit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BalanceAsOf = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FiskilTransactionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PrimaryCategory = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SecondaryCategory = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MerchantName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transactions_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_transactions_AccountId",
                table: "transactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_PostedAt",
                table: "transactions",
                column: "PostedAt");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_UserId",
                table: "transactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_UserId_FiskilTransactionId",
                table: "transactions",
                columns: new[] { "UserId", "FiskilTransactionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
