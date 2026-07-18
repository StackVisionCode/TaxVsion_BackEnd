using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePermissionPlanGuardrails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAssignableByTenant",
                table: "Permissions",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<int>(
                name: "MinPlanTier",
                table: "Permissions",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { false, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000005"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000006"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000007"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { false, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000008"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { false, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000009"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { false, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000010"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000011"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000012"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000013"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000014"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000015"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000016"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000017"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000018"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000019"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000020"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000021"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000022"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000023"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000024"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000025"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000026"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000027"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000028"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000029"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000030"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000031"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000032"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000033"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000034"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000035"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000036"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000037"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000038"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000039"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000040"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000041"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000042"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000043"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000044"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000063"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 0 }
            );

            // Las 18 filas de Communication (...045-...062) YA EXISTEN en toda base desplegada:
            // se insertaron con SQL directo en AddCommunicationPermissions (2026-07-10), sin
            // pasar nunca por HasData/PermissionCatalog (desfase documentado ahí mismo). Ahora
            // que PermissionCatalog.All las incluye, EF las generó como InsertData — se
            // reescribe a mano como UpdateData para solo completar las 2 columnas nuevas,
            // igual que el resto de filas de arriba, sin chocar con las que ya están en BD.
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000045"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000046"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000047"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000048"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000049"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000050"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000051"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000052"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000053"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000054"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000055"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000056"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000057"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000058"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000059"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000060"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000061"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000062"),
                columns: new[] { "IsAssignableByTenant", "MinPlanTier" },
                values: new object[] { true, 1 }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin DeleteData para ...045-...062: esas 18 filas de Communication ya existían
            // antes de esta migración (ver comentario en Up); revertir esta migración no debe
            // borrarlas, solo las columnas que ella agregó (DropColumn abajo ya lo cubre).
            migrationBuilder.DropColumn(name: "IsAssignableByTenant", table: "Permissions");

            migrationBuilder.DropColumn(name: "MinPlanTier", table: "Permissions");
        }
    }
}
