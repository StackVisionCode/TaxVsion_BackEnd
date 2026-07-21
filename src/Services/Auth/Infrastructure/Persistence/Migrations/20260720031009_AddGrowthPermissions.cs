using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGrowthPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[]
                {
                    "Id",
                    "Code",
                    "Description",
                    "IsAssignableByTenant",
                    "IsCustomerPortal",
                    "MinPlanTier",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000123"),
                        "codes.code.read",
                        "Ver códigos del propio tenant",
                        true,
                        false,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000124"),
                        "codes.code.manage",
                        "Gestionar códigos del propio tenant",
                        true,
                        false,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000125"),
                        "codes.code.issue",
                        "Emitir códigos de beneficio",
                        true,
                        false,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000126"),
                        "codes.code.activate",
                        "Activar códigos",
                        true,
                        false,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000127"),
                        "codes.code.revoke",
                        "Revocar códigos",
                        true,
                        false,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000128"),
                        "codes.audit.read",
                        "Consultar auditoría de códigos",
                        true,
                        false,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000129"),
                        "codes.redemption.read",
                        "Consultar redemptions",
                        true,
                        false,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000130"),
                        "codes.compensation.manage",
                        "Gestionar compensaciones promocionales",
                        false,
                        false,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000131"),
                        "referrals.own.read",
                        "Ver referidos propios",
                        true,
                        false,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000132"),
                        "referrals.program.read",
                        "Ver programas de referidos",
                        true,
                        false,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000133"),
                        "referrals.program.manage",
                        "Gestionar programas de referidos",
                        true,
                        false,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000134"),
                        "referrals.attribution.read",
                        "Consultar atribuciones",
                        true,
                        false,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000135"),
                        "referrals.fraud.read",
                        "Consultar revisiones antifraude",
                        true,
                        false,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000136"),
                        "referrals.fraud.manage",
                        "Gestionar revisiones antifraude",
                        false,
                        false,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000137"),
                        "referrals.reward.read",
                        "Consultar rewards",
                        true,
                        false,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000138"),
                        "referrals.reward.manage",
                        "Gestionar rewards no monetarios",
                        false,
                        false,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000139"),
                        "referrals.audit.read",
                        "Consultar auditoría de referidos",
                        true,
                        false,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000140"),
                        "growth.admin.cross_tenant",
                        "Operar recursos Growth de cualquier tenant",
                        false,
                        false,
                        0,
                        "growth",
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000123")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000124")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000125")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000126")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000127")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000128")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000129")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000130")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000131")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000132")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000133")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000134")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000135")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000136")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000137")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000138")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000139")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000140")
            );
        }
    }
}
