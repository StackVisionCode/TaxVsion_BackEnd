using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralAndGiftcardPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000123"),
                columns: new[] { "AllowedActorTypes", "Description", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[]
                {
                    "PlatformAdmin",
                    "View giftcards and promotional codes (platform only)",
                    false,
                    true,
                }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000124"),
                columns: new[] { "AllowedActorTypes", "Description", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[]
                {
                    "PlatformAdmin",
                    "Create and edit giftcards and promotional codes (platform only)",
                    false,
                    true,
                }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000126"),
                columns: new[] { "AllowedActorTypes", "Description", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[]
                {
                    "PlatformAdmin",
                    "Activate giftcards and promotional codes (platform only)",
                    false,
                    true,
                }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000127"),
                columns: new[] { "AllowedActorTypes", "Description", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[]
                {
                    "PlatformAdmin",
                    "Revoke giftcards and promotional codes (platform only)",
                    false,
                    true,
                }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000131"),
                column: "Description",
                value: "View, copy and share your office's referral code"
            );

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[]
                {
                    "Id",
                    "AllowedActorTypes",
                    "Code",
                    "Description",
                    "GatedModule",
                    "IsAssignableByTenant",
                    "IsCustomerPortal",
                    "IsDangerous",
                    "IsReserved",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000226"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "referrals.code.manage",
                        "Generate and regenerate the office's referral code",
                        null,
                        true,
                        false,
                        false,
                        false,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000227"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "referrals.program.read",
                        "View the referral program terms",
                        null,
                        true,
                        false,
                        false,
                        false,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000221"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "referrals.reward.read",
                        "View the office's referrals, share history and referral rewards",
                        null,
                        true,
                        false,
                        false,
                        false,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000222"),
                        "PlatformAdmin",
                        "referrals.program.manage",
                        "Create, version, activate and suspend the referral program (platform only)",
                        null,
                        false,
                        false,
                        false,
                        false,
                        "referrals",
                        true,
                    },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000226")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000227")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000221")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000222")
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000123"),
                columns: new[] { "AllowedActorTypes", "Description", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[]
                {
                    "TenantEmployee,TenantAdmin,PlatformAdmin",
                    "View the office's codes",
                    true,
                    false,
                }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000124"),
                columns: new[] { "AllowedActorTypes", "Description", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[]
                {
                    "TenantEmployee,TenantAdmin,PlatformAdmin",
                    "Manage the office's codes",
                    true,
                    false,
                }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000126"),
                columns: new[] { "AllowedActorTypes", "Description", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[] { "TenantEmployee,TenantAdmin,PlatformAdmin", "Activate codes", true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000127"),
                columns: new[] { "AllowedActorTypes", "Description", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[] { "TenantEmployee,TenantAdmin,PlatformAdmin", "Revoke codes", true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000131"),
                column: "Description",
                value: "View your own referrals"
            );
        }
    }
}
