using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RbacFase8UserPermissionsProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "permissions");

            migrationBuilder.CreateTable(
                name: "RolePermissionsProjections",
                schema: "permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PermissionCodesJson = table.Column<string>(
                        type: "nvarchar(4000)",
                        maxLength: 4000,
                        nullable: false
                    ),
                    PermissionsVersion = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissionsProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "UserPermissionsProjections",
                schema: "permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionsVersion = table.Column<int>(type: "int", nullable: false),
                    PermissionCodesJson = table.Column<string>(
                        type: "nvarchar(4000)",
                        maxLength: 4000,
                        nullable: false
                    ),
                    RoleIdsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissionsProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissionsProjections_TenantId",
                schema: "permissions",
                table: "RolePermissionsProjections",
                column: "TenantId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionsProjections_TenantId_IsActive",
                schema: "permissions",
                table: "UserPermissionsProjections",
                columns: new[] { "TenantId", "IsActive" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionsProjections_TenantId_UserId",
                schema: "permissions",
                table: "UserPermissionsProjections",
                columns: new[] { "TenantId", "UserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RolePermissionsProjections", schema: "permissions");

            migrationBuilder.DropTable(name: "UserPermissionsProjections", schema: "permissions");
        }
    }
}
