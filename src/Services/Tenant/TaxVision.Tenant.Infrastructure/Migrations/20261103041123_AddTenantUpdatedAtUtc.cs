using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantUpdatedAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)
            );

            // Oficinas existentes: el último cambio conocido es el alta. Compilación diferida porque la
            // columna nace en esta misma migración.
            migrationBuilder.Sql(
                "EXEC sp_executesql N'UPDATE [Tenants] SET [UpdatedAtUtc] = [CreatedAtUtc] WHERE [UpdatedAtUtc] < [CreatedAtUtc]';"
            );

            migrationBuilder.UpdateData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("8f58a521-4c25-4d91-9f4e-7ad5df14c001"),
                column: "UpdatedAtUtc",
                value: new DateTime(2026, 6, 27, 0, 0, 0, 0, DateTimeKind.Utc)
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "UpdatedAtUtc", table: "Tenants");
        }
    }
}
