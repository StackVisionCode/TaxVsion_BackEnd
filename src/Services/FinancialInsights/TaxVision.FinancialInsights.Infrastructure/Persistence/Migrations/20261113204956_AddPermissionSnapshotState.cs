using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.FinancialInsights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionSnapshotState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasPermissionsSnapshot",
                table: "UserPermissionsProjections",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LifecycleUpdatedAtUtc",
                table: "UserPermissionsProjections",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasPermissionsSnapshot",
                table: "UserPermissionsProjections");

            migrationBuilder.DropColumn(
                name: "LifecycleUpdatedAtUtc",
                table: "UserPermissionsProjections");
        }
    }
}
