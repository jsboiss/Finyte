using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finyte.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClerkFamilyIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClerkOrganizationId",
                table: "tenants",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE tenants
                SET "ClerkOrganizationId" = 'legacy_' || replace("Id"::text, '-', '')
                WHERE "ClerkOrganizationId" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ClerkOrganizationId",
                table: "tenants",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClerkMembershipId",
                table: "tenant_members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_ClerkOrganizationId",
                table: "tenants",
                column: "ClerkOrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_members_ClerkMembershipId",
                table: "tenant_members",
                column: "ClerkMembershipId",
                unique: true,
                filter: "\"ClerkMembershipId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tenants_ClerkOrganizationId",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "IX_tenant_members_ClerkMembershipId",
                table: "tenant_members");

            migrationBuilder.DropColumn(
                name: "ClerkOrganizationId",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "ClerkMembershipId",
                table: "tenant_members");
        }
    }
}
