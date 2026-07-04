using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthzEntitlements.Governance.Service.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessGrantRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AccessPackageCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequestedDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SodOutcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SodReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessGrantRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AccessPackageCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DefaultDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessReviewCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TenantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessReviewCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Principals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Principals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessGrantRole",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessGrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessGrantRole", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessGrantRole_AccessGrants_AccessGrantId",
                        column: x => x.AccessGrantId,
                        principalTable: "AccessGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessPackageRole",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessPackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessPackageRole", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessPackageRole_AccessPackages_AccessPackageId",
                        column: x => x.AccessPackageId,
                        principalTable: "AccessPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessReviewItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessGrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReviewedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessReviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessReviewItems_AccessReviewCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "AccessReviewCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrincipalRole",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RoleName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrincipalRole", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrincipalRole_Principals_PrincipalId",
                        column: x => x.PrincipalId,
                        principalTable: "Principals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrantRequests_PrincipalId",
                table: "AccessGrantRequests",
                column: "PrincipalId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrantRequests_Status",
                table: "AccessGrantRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrantRole_AccessGrantId",
                table: "AccessGrantRole",
                column: "AccessGrantId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_PrincipalId",
                table: "AccessGrants",
                column: "PrincipalId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_TenantCode",
                table: "AccessGrants",
                column: "TenantCode");

            migrationBuilder.CreateIndex(
                name: "IX_AccessPackageRole_AccessPackageId_RoleName",
                table: "AccessPackageRole",
                columns: new[] { "AccessPackageId", "RoleName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessPackages_Code",
                table: "AccessPackages",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessReviewCampaigns_TenantCode",
                table: "AccessReviewCampaigns",
                column: "TenantCode");

            migrationBuilder.CreateIndex(
                name: "IX_AccessReviewItems_AccessGrantId",
                table: "AccessReviewItems",
                column: "AccessGrantId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessReviewItems_CampaignId",
                table: "AccessReviewItems",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_PrincipalRole_PrincipalId_RoleName",
                table: "PrincipalRole",
                columns: new[] { "PrincipalId", "RoleName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessGrantRequests");

            migrationBuilder.DropTable(
                name: "AccessGrantRole");

            migrationBuilder.DropTable(
                name: "AccessPackageRole");

            migrationBuilder.DropTable(
                name: "AccessReviewItems");

            migrationBuilder.DropTable(
                name: "PrincipalRole");

            migrationBuilder.DropTable(
                name: "AccessGrants");

            migrationBuilder.DropTable(
                name: "AccessPackages");

            migrationBuilder.DropTable(
                name: "AccessReviewCampaigns");

            migrationBuilder.DropTable(
                name: "Principals");
        }
    }
}
