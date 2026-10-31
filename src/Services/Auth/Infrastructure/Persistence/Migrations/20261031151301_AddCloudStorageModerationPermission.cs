using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudStorageModerationPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "AllowedActorTypes", "Code", "Description", "GatedModule", "IsAssignableByTenant", "IsCustomerPortal", "IsDangerous", "IsReserved", "MinPlanTier", "Module", "PlatformOnly" },
                values: new object[] { new Guid("a1000000-0000-0000-0000-000000000217"), "PlatformAdmin", "cloudstorage.moderation.manage", "Review flagged uploads and manage the content moderation policy", "documents", false, false, false, false, 0, "cloudstorage", true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000217"));
        }
    }
}
