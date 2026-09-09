using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountTypeOverride",
                table: "accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomName",
                table: "accounts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeInAnalyticsOverride",
                table: "accounts",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManualBalanceVersion",
                table: "accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreferencesVersion",
                table: "accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            InvalidateProjections(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountTypeOverride",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "CustomName",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "IncludeInAnalyticsOverride",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "ManualBalanceVersion",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "PreferencesVersion",
                table: "accounts");

            InvalidateProjections(migrationBuilder);
        }

        private static void InvalidateProjections(MigrationBuilder migrationBuilder)
        {
            // Classification defaults change combined spending even before a family edits a preference.
            migrationBuilder.Sql("""
                UPDATE tenants SET "FinancialDataVersion" = "FinancialDataVersion" + 1;
                UPDATE overview_projections
                SET "Status" = 'pending', "Generation" = "Generation" + 1,
                    "LastError" = NULL, "TemporalWorkflowId" = NULL, "DispatchedAt" = NULL,
                    "InvalidatedAt" = CURRENT_TIMESTAMP, "UpdatedAt" = CURRENT_TIMESTAMP;
                """);
        }
    }
}
