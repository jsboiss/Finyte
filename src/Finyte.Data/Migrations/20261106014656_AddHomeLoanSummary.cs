using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeLoanSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "home_loan_category_choices",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_home_loan_category_choices", x => new { x.TenantId, x.TagId });
                    table.ForeignKey(
                        name: "FK_home_loan_category_choices_transaction_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "transaction_tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "home_loan_income_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Applicant = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_home_loan_income_sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "home_loan_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstApplicant = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SecondApplicant = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    SpendingAccountIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    SavingsAccountIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_home_loan_profiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_home_loan_category_choices_TagId",
                table: "home_loan_category_choices",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_home_loan_income_sources_TenantId_MatchKey",
                table: "home_loan_income_sources",
                columns: new[] { "TenantId", "MatchKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_home_loan_profiles_TenantId",
                table: "home_loan_profiles",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "home_loan_category_choices");

            migrationBuilder.DropTable(
                name: "home_loan_income_sources");

            migrationBuilder.DropTable(
                name: "home_loan_profiles");
        }
    }
}
