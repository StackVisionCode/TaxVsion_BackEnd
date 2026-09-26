using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPermissionDenyReasonAndExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "UserPermissionDenies",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "UserPermissionDenies",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionDenies_ExpiresAtUtc",
                table: "UserPermissionDenies",
                column: "ExpiresAtUtc"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_UserPermissionDenies_ExpiresAtUtc", table: "UserPermissionDenies");

            migrationBuilder.DropColumn(name: "ExpiresAtUtc", table: "UserPermissionDenies");

            migrationBuilder.DropColumn(name: "Reason", table: "UserPermissionDenies");
        }
    }
}
