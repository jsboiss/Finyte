using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOverviewProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "overview_projections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    MonthKey = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceWatermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overview_projections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_overview_projections_TenantId_AccountId_MonthKey",
                table: "overview_projections",
                columns: new[] { "TenantId", "AccountId", "MonthKey" },
                unique: true,
                filter: "\"AccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_overview_projections_TenantId_MonthKey",
                table: "overview_projections",
                columns: new[] { "TenantId", "MonthKey" },
                unique: true,
                filter: "\"AccountId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "overview_projections");
        }
    }
}
