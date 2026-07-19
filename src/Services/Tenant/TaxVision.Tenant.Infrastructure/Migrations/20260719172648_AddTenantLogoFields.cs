using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLogoFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoContentType",
                table: "Tenants",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "LogoFileId",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(name: "LogoHeight", table: "Tenants", type: "int", nullable: true);

            migrationBuilder.AddColumn<long>(name: "LogoSizeBytes", table: "Tenants", type: "bigint", nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LogoUpdatedAtUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(name: "LogoWidth", table: "Tenants", type: "int", nullable: true);

            migrationBuilder.UpdateData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("8f58a521-4c25-4d91-9f4e-7ad5df14c001"),
                columns: new[]
                {
                    "LogoContentType",
                    "LogoFileId",
                    "LogoHeight",
                    "LogoSizeBytes",
                    "LogoUpdatedAtUtc",
                    "LogoWidth",
                },
                values: new object[] { null, null, null, null, null, null }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LogoContentType", table: "Tenants");

            migrationBuilder.DropColumn(name: "LogoFileId", table: "Tenants");

            migrationBuilder.DropColumn(name: "LogoHeight", table: "Tenants");

            migrationBuilder.DropColumn(name: "LogoSizeBytes", table: "Tenants");

            migrationBuilder.DropColumn(name: "LogoUpdatedAtUtc", table: "Tenants");

            migrationBuilder.DropColumn(name: "LogoWidth", table: "Tenants");
        }
    }
}
