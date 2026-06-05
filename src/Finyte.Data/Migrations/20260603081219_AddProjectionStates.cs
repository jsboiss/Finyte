using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectionStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projection_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectionKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ScopeJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsStale = table.Column<bool>(type: "boolean", nullable: false),
                    StaleAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StaleReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LastStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSucceededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projection_states", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_projection_states_TenantId_ProjectionKey_ScopeKey",
                table: "projection_states",
                columns: new[] { "TenantId", "ProjectionKey", "ScopeKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "projection_states");
        }
    }
}
