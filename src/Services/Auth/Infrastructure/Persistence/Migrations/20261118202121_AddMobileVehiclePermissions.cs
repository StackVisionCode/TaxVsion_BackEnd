using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileVehiclePermissions : Migration
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
                        new Guid("3d708b3c-41cb-433e-b0b1-dd7a5cece728"),
                        "CustomerPortal",
                        "mobile.vehicles.manage",
                        "Maintain own vehicles",
                        null,
                        true,
                        true,
                        false,
                        false,
                        "mobile",
                        false,
                    },
                    {
                        new Guid("d73691a8-90ae-47e1-b776-118b87cc86bd"),
                        "CustomerPortal",
                        "mobile.vehicles.read",
                        "Read own vehicles",
                        null,
                        true,
                        true,
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
                keyValue: new Guid("3d708b3c-41cb-433e-b0b1-dd7a5cece728")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("d73691a8-90ae-47e1-b776-118b87cc86bd")
            );
        }
    }
}
