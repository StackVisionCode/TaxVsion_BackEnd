using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Scribe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRowVersionToTemplatesAndSubDomainOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SubDomainChangedAtUtc",
                table: "TenantProfileRefs",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "EmailTemplates",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "EmailLayouts",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SubDomainChangedAtUtc", table: "TenantProfileRefs");

            migrationBuilder.DropColumn(name: "RowVersion", table: "EmailTemplates");

            migrationBuilder.DropColumn(name: "RowVersion", table: "EmailLayouts");
        }
    }
}
