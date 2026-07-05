using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthzEntitlements.Audit.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestSnapshot",
                table: "AuditEntries",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestSnapshot",
                table: "AuditEntries");
        }
    }
}
