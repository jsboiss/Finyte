using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurring_discovery_decisions",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DismissedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_discovery_decisions", x => new { x.TenantId, x.CandidateKey });
                });

            migrationBuilder.CreateTable(
                name: "recurring_payment_series",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Cadence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AnchorDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_payment_series", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recurring_payment_aliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    Field = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NormalizedValue = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_payment_aliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recurring_payment_aliases_recurring_payment_series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "recurring_payment_series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recurring_payment_decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurrenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_payment_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recurring_payment_decisions_recurring_payment_series_Series~",
                        column: x => x.SeriesId,
                        principalTable: "recurring_payment_series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recurring_payment_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurrenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_payment_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recurring_payment_reviews_recurring_payment_series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "recurring_payment_series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recurring_payment_aliases_SeriesId_Field_NormalizedValue",
                table: "recurring_payment_aliases",
                columns: new[] { "SeriesId", "Field", "NormalizedValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recurring_payment_decisions_SeriesId_OccurrenceDate",
                table: "recurring_payment_decisions",
                columns: new[] { "SeriesId", "OccurrenceDate" },
                unique: true,
                filter: "\"Status\" = 'confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_payment_decisions_TenantId_SeriesId_TransactionId~",
                table: "recurring_payment_decisions",
                columns: new[] { "TenantId", "SeriesId", "TransactionId", "OccurrenceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recurring_payment_decisions_TenantId_TransactionId",
                table: "recurring_payment_decisions",
                columns: new[] { "TenantId", "TransactionId" },
                unique: true,
                filter: "\"Status\" = 'confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_payment_reviews_SeriesId",
                table: "recurring_payment_reviews",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_payment_reviews_TenantId_SeriesId_ReviewedAt",
                table: "recurring_payment_reviews",
                columns: new[] { "TenantId", "SeriesId", "ReviewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_recurring_payment_series_TenantId_Name",
                table: "recurring_payment_series",
                columns: new[] { "TenantId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_discovery_decisions");

            migrationBuilder.DropTable(
                name: "recurring_payment_aliases");

            migrationBuilder.DropTable(
                name: "recurring_payment_decisions");

            migrationBuilder.DropTable(
                name: "recurring_payment_reviews");

            migrationBuilder.DropTable(
                name: "recurring_payment_series");
        }
    }
}
