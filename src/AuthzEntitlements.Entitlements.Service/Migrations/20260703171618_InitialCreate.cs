using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthzEntitlements.Entitlements.Service.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Tier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SeatLimit = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Tier);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PlanTier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    QuotaKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Used = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanModules",
                columns: table => new
                {
                    PlanTier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanModules", x => new { x.PlanTier, x.ModuleKey });
                    table.ForeignKey(
                        name: "FK_PlanModules_Plans_PlanTier",
                        column: x => x.PlanTier,
                        principalTable: "Plans",
                        principalColumn: "Tier",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanQuotas",
                columns: table => new
                {
                    PlanTier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    QuotaKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Limit = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanQuotas", x => new { x.PlanTier, x.QuotaKey });
                    table.ForeignKey(
                        name: "FK_PlanQuotas_Plans_PlanTier",
                        column: x => x.PlanTier,
                        principalTable: "Plans",
                        principalColumn: "Tier",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeatAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeatAssignments_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeatAssignments_SubscriptionId_UserId",
                table: "SeatAssignments",
                columns: new[] { "SubscriptionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_TenantCode",
                table: "Subscriptions",
                column: "TenantCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageCounters_TenantCode_QuotaKey_PeriodKey",
                table: "UsageCounters",
                columns: new[] { "TenantCode", "QuotaKey", "PeriodKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanModules");

            migrationBuilder.DropTable(
                name: "PlanQuotas");

            migrationBuilder.DropTable(
                name: "SeatAssignments");

            migrationBuilder.DropTable(
                name: "UsageCounters");

            migrationBuilder.DropTable(
                name: "Plans");

            migrationBuilder.DropTable(
                name: "Subscriptions");
        }
    }
}
