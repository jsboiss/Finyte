using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagProvenanceAndExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MerchantRuleId",
                table: "transaction_tag_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "transaction_tag_assignments",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.CreateTable(
                name: "transaction_tag_exclusions",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_tag_exclusions", x => new { x.TransactionId, x.TagId });
                    table.ForeignKey(
                        name: "FK_transaction_tag_exclusions_transaction_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "transaction_tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transaction_tag_exclusions_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transaction_tag_assignments_MerchantRuleId",
                table: "transaction_tag_assignments",
                column: "MerchantRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_tag_exclusions_TagId",
                table: "transaction_tag_exclusions",
                column: "TagId");

            migrationBuilder.AddForeignKey(
                name: "FK_transaction_tag_assignments_merchant_tag_rules_MerchantRule~",
                table: "transaction_tag_assignments",
                column: "MerchantRuleId",
                principalTable: "merchant_tag_rules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transaction_tag_assignments_merchant_tag_rules_MerchantRule~",
                table: "transaction_tag_assignments");

            migrationBuilder.DropTable(
                name: "transaction_tag_exclusions");

            migrationBuilder.DropIndex(
                name: "IX_transaction_tag_assignments_MerchantRuleId",
                table: "transaction_tag_assignments");

            migrationBuilder.DropColumn(
                name: "MerchantRuleId",
                table: "transaction_tag_assignments");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "transaction_tag_assignments");
        }
    }
}
