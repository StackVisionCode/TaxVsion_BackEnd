using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptFileIdIndexAndManagePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "AllowedActorTypes", "Code", "Description", "IsAssignableByTenant", "IsCustomerPortal", "IsDangerous", "MinPlanTier", "Module", "PlatformOnly" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-00000000009a"), "TenantEmployee,TenantAdmin,PlatformAdmin", "correspondence.manage", "Archivar, enviar a papelera, restaurar y borrar definitivamente correspondencia", true, false, false, 0, "correspondence", false },
                    { new Guid("a1000000-0000-0000-0000-00000000009b"), "TenantEmployee,TenantAdmin,PlatformAdmin", "signature.legal.manage", "Colocar y levantar retención legal (legal hold) sobre una firma", true, false, false, 0, "signature", false }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantOnboardings_ReceiptFileId",
                table: "TenantOnboardings",
                column: "ReceiptFileId",
                unique: true,
                filter: "[ReceiptFileId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantOnboardings_ReceiptFileId",
                table: "TenantOnboardings");

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-00000000009a"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-00000000009b"));
        }
    }
}
