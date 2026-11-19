using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Mobile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserPermissionsProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionsVersion = table.Column<int>(type: "int", nullable: false),
                    PermissionCodesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoleIdsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    HasPermissionsSnapshot = table.Column<bool>(type: "bit", nullable: false),
                    LifecycleUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissionsProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Plate = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Make = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Year = table.Column<int>(type: "int", nullable: true),
                    Color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Photo = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CurrentMileage = table.Column<decimal>(
                        type: "decimal(11,2)",
                        precision: 11,
                        scale: 2,
                        nullable: false
                    ),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    PlateKey = table.Column<string>(
                        type: "nvarchar(32)",
                        maxLength: 32,
                        nullable: true,
                        computedColumnSql: "[Plate]",
                        stored: true
                    ),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.CheckConstraint(
                        "CK_Vehicles_CurrentMileage",
                        "[CurrentMileage] >= 0 AND [CurrentMileage] <= 999999999.99"
                    );
                    table.CheckConstraint("CK_Vehicles_Year", "[Year] IS NULL OR ([Year] >= 1886 AND [Year] <= 2100)");
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionsProjections_TenantId_UserId",
                table: "UserPermissionsProjections",
                columns: new[] { "TenantId", "UserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_CustomerId_Id",
                table: "Vehicles",
                columns: new[] { "TenantId", "CustomerId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_CustomerId_IsDefault",
                table: "Vehicles",
                columns: new[] { "TenantId", "CustomerId", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_CustomerId_PlateKey",
                table: "Vehicles",
                columns: new[] { "TenantId", "CustomerId", "PlateKey" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserPermissionsProjections");

            migrationBuilder.DropTable(name: "Vehicles");
        }
    }
}
