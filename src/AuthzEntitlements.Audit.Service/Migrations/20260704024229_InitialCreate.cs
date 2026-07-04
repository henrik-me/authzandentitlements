using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthzEntitlements.Audit.Service.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Tenant = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Producer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PrevHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    RowHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Producer",
                table: "AuditEntries",
                column: "Producer");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_RowHash",
                table: "AuditEntries",
                column: "RowHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_SubjectId",
                table: "AuditEntries",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");
        }
    }
}
