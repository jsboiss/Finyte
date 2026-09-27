using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringSeriesKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "recurring_payment_series",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "subscription");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "recurring_payment_series");
        }
    }
}
