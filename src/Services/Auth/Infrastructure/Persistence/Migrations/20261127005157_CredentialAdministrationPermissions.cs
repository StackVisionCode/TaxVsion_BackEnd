using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CredentialAdministrationPermissions : Migration
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
                        new Guid("a1000000-0000-0000-0000-000000000234"),
                        "TenantAdmin",
                        "users.password.send_reset",
                        "Send or cancel a password reset link to a team member",
                        null,
                        false,
                        false,
                        true,
                        true,
                        "users",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000235"),
                        "TenantAdmin",
                        "users.password.require_change",
                        "Require a team member to change their password",
                        null,
                        false,
                        false,
                        true,
                        true,
                        "users",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000236"),
                        "TenantAdmin",
                        "users.credentials.secure",
                        "Secure a team member's account (sign out and force a reset)",
                        null,
                        false,
                        false,
                        true,
                        true,
                        "users",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000237"),
                        "TenantAdmin",
                        "users.mfa.reset",
                        "Reset a team member's two-step verification",
                        null,
                        false,
                        false,
                        true,
                        true,
                        "users",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000238"),
                        "TenantAdmin",
                        "customers.portal_credentials.send_reset",
                        "Send or cancel a client portal password reset link",
                        "customers",
                        false,
                        false,
                        true,
                        true,
                        "customers",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000239"),
                        "TenantAdmin",
                        "customers.portal_credentials.require_change",
                        "Require a client to change their portal password",
                        "customers",
                        false,
                        false,
                        true,
                        true,
                        "customers",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-00000000023a"),
                        "TenantAdmin",
                        "customers.portal_credentials.secure",
                        "Secure a client's portal account (sign out and force a reset)",
                        "customers",
                        false,
                        false,
                        true,
                        true,
                        "customers",
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000234")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000235")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000236")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000237")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000238")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000239")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-00000000023a")
            );
        }
    }
}
