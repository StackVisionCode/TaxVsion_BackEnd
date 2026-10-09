using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Movement = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DeltaPostedMicros = table.Column<long>(type: "bigint", nullable: false),
                    DeltaHeldMicros = table.Column<long>(type: "bigint", nullable: false),
                    PostedAfterMicros = table.Column<long>(type: "bigint", nullable: false),
                    HeldAfterMicros = table.Column<long>(type: "bigint", nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "RolePermissionsProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PermissionCodesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissionsProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    PostedMicros = table.Column<long>(type: "bigint", nullable: false),
                    HeldMicros = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                    table.CheckConstraint(
                        "CK_Wallets_Balances",
                        "[PostedMicros] >= [HeldMicros] AND [HeldMicros] >= 0"
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_CreatedAtUtc",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "CreatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_OperationKey",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "OperationKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissionsProjections_TenantId",
                table: "RolePermissionsProjections",
                column: "TenantId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantPlanCodeProjections_TenantId",
                table: "TenantPlanCodeProjections",
                column: "TenantId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionsProjections_TenantId_IsActive",
                table: "UserPermissionsProjections",
                columns: new[] { "TenantId", "IsActive" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionsProjections_TenantId_UserId",
                table: "UserPermissionsProjections",
                columns: new[] { "TenantId", "UserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_TenantId_Currency",
                table: "Wallets",
                columns: new[] { "TenantId", "Currency" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LedgerEntries");

            migrationBuilder.DropTable(name: "RolePermissionsProjections");

            migrationBuilder.DropTable(name: "TenantPlanCodeProjections");

            migrationBuilder.DropTable(name: "UserPermissionsProjections");

            migrationBuilder.DropTable(name: "Wallets");
        }
    }
}
