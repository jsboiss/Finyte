using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeLoanExcludedAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "ExcludedAccountIds",
                table: "home_loan_profiles",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.Sql("""
                UPDATE home_loan_profiles p
                SET "ExcludedAccountIds" = ARRAY(
                    SELECT a."Id" FROM accounts a
                    WHERE a."TenantId" = p."TenantId"
                    AND NOT a."Id" = ANY(p."SpendingAccountIds")
                    AND NOT a."Id" = ANY(p."SavingsAccountIds"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludedAccountIds",
                table: "home_loan_profiles");
        }
    }
}
