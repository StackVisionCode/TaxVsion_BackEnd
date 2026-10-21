using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReserveUnenforcedClientFormsPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000199"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000201"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000202"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000203"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000204"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000205"),
                column: "IsReserved",
                value: true);

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000208"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000209"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000210"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000211"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000199"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000201"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000202"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000203"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000204"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000205"),
                column: "IsReserved",
                value: false);

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000208"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000209"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000210"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000211"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false });
        }
    }
}
