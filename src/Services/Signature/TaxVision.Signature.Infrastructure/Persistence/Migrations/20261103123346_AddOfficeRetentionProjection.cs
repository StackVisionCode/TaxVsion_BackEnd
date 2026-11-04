using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficeRetentionProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OfficeRetention_UpdatedAtUtc",
                table: "TenantSignatureSettings",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "OfficeRetention_Years",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OfficeRetention_UpdatedAtUtc", table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(name: "OfficeRetention_Years", table: "TenantSignatureSettings");
        }
    }
}
