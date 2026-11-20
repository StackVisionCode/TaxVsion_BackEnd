using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MapMobilePermissionsToMileageModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("3d708b3c-41cb-433e-b0b1-dd7a5cece728"),
                column: "GatedModule",
                value: "mileage"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a01"),
                column: "GatedModule",
                value: "mileage"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a02"),
                column: "GatedModule",
                value: "mileage"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a03"),
                column: "GatedModule",
                value: "mileage"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a04"),
                column: "GatedModule",
                value: "mileage"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a05"),
                column: "GatedModule",
                value: "mileage"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("d73691a8-90ae-47e1-b776-118b87cc86bd"),
                column: "GatedModule",
                value: "mileage"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("3d708b3c-41cb-433e-b0b1-dd7a5cece728"),
                column: "GatedModule",
                value: null
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a01"),
                column: "GatedModule",
                value: null
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a02"),
                column: "GatedModule",
                value: null
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a03"),
                column: "GatedModule",
                value: null
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a04"),
                column: "GatedModule",
                value: null
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bf063f60-2bed-46f7-8c97-e2381b6f4a05"),
                column: "GatedModule",
                value: null
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("d73691a8-90ae-47e1-b776-118b87cc86bd"),
                column: "GatedModule",
                value: null
            );
        }
    }
}
