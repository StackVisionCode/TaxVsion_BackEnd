using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunicationPlatformPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    "MinPlanTier",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000213"),
                        "PlatformAdmin",
                        "communication.platform.manage",
                        "Manage platform-wide communication settings",
                        "comms",
                        false,
                        false,
                        false,
                        false,
                        0,
                        "communication",
                        true,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000214"),
                        "PlatformAdmin",
                        "communication.constraints.manage",
                        "Manage per-office communication ceilings",
                        "comms",
                        false,
                        false,
                        false,
                        false,
                        0,
                        "communication",
                        true,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000215"),
                        "PlatformAdmin",
                        "communication.platform.analytics.read",
                        "View platform-wide communication analytics",
                        "comms",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "communication",
                        true,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000216"),
                        "PlatformAdmin",
                        "cloudstorage.upload_policy.manage",
                        "Manage upload size policies per folder, plan and office",
                        "documents",
                        false,
                        false,
                        false,
                        false,
                        0,
                        "cloudstorage",
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000213")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000214")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000215")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000216")
            );
        }
    }
}
