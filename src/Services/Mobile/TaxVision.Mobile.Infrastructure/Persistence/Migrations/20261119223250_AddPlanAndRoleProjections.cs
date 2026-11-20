using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Mobile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanAndRoleProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RolePermissionsProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PermissionsVersion = table.Column<int>(type: "int", nullable: false),
                    PermissionCodesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissionsProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantPlanCodeProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RevisionNumber = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EnabledModulesJson = table.Column<string>(
                        type: "nvarchar(max)",
                        nullable: false,
                        defaultValue: "[]"
                    ),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPlanCodeProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissionsProjections_TenantId_Id",
                table: "RolePermissionsProjections",
                columns: new[] { "TenantId", "Id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantPlanCodeProjections_TenantId",
                table: "TenantPlanCodeProjections",
                column: "TenantId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RolePermissionsProjections");

            migrationBuilder.DropTable(name: "TenantPlanCodeProjections");
        }
    }
}
