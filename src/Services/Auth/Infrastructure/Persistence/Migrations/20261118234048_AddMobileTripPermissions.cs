using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileTripPermissions : Migration
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
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a01"),
                        "CustomerPortal",
                        "mobile.trips.read",
                        "Read own trips and statistics",
                        null,
                        true,
                        true,
                        false,
                        false,
                        "mobile",
                        false,
                    },
                    {
                        new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a02"),
                        "CustomerPortal",
                        "mobile.trips.manage",
                        "Maintain own trips",
                        null,
                        true,
                        true,
                        false,
                        false,
                        "mobile",
                        false,
                    },
                    {
                        new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a03"),
                        "CustomerPortal",
                        "mobile.trips.sync",
                        "Synchronize own GPS trips",
                        null,
                        true,
                        true,
                        false,
                        false,
                        "mobile",
                        false,
                    },
                    {
                        new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a04"),
                        "CustomerPortal,TenantAdmin,PlatformAdmin",
                        "mobile.configuration.read",
                        "Read mileage configuration",
                        null,
                        true,
                        false,
                        false,
                        false,
                        "mobile",
                        false,
                    },
                    {
                        new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a05"),
                        "TenantAdmin,PlatformAdmin",
                        "mobile.rates.manage",
                        "Configure office mileage estimates",
                        null,
                        true,
                        false,
                        false,
                        false,
                        "mobile",
                        false,
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
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a01")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a02")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a03")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a04")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a05")
            );
        }
    }
}
