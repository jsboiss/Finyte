using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transaction_tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "merchant_tag_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MerchantKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_tag_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_merchant_tag_rules_transaction_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "transaction_tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transaction_tag_assignments",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_tag_assignments", x => new { x.TransactionId, x.TagId });
                    table.ForeignKey(
                        name: "FK_transaction_tag_assignments_transaction_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "transaction_tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transaction_tag_assignments_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_merchant_tag_rules_TagId",
                table: "merchant_tag_rules",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_merchant_tag_rules_TenantId",
                table: "merchant_tag_rules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_merchant_tag_rules_TenantId_MerchantKey_TagId",
                table: "merchant_tag_rules",
                columns: new[] { "TenantId", "MerchantKey", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transaction_tag_assignments_TagId",
                table: "transaction_tag_assignments",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_tags_TenantId",
                table: "transaction_tags",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_tags_TenantId_Name",
                table: "transaction_tags",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merchant_tag_rules");

            migrationBuilder.DropTable(
                name: "transaction_tag_assignments");

            migrationBuilder.DropTable(
                name: "transaction_tags");
        }
    }
}
