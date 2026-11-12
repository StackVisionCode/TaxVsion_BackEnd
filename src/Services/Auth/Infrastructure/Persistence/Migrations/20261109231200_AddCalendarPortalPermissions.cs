using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarPortalPermissions : Migration
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
                        new Guid("a1000000-0000-0000-0000-000000000218"),
                        "CustomerPortal",
                        "calendar.portal.read",
                        "The client sees their appointments in the portal",
                        "planner",
                        true,
                        true,
                        false,
                        false,
                        0,
                        "calendar",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000219"),
                        "CustomerPortal",
                        "calendar.portal.book",
                        "The client books, cancels and reschedules their own appointments",
                        "planner",
                        true,
                        true,
                        false,
                        false,
                        0,
                        "calendar",
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000218")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000219")
            );
        }
    }
}
