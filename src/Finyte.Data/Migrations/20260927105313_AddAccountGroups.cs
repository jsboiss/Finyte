using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "account_group_members",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_group_members", x => new { x.GroupId, x.AccountId });
                    table.ForeignKey(
                        name: "FK_account_group_members_account_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "account_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_account_group_members_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_group_members_AccountId",
                table: "account_group_members",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_account_groups_TenantId_Name",
                table: "account_groups",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_group_members");

            migrationBuilder.DropTable(
                name: "account_groups");
        }
    }
}
