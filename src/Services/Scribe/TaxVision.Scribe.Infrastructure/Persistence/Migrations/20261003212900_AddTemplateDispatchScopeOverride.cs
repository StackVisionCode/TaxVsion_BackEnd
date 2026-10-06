using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Scribe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateDispatchScopeOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DispatchScopeOverride",
                table: "EmailTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "DispatchScopeOverrideAtUtc",
                table: "EmailTemplates",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DispatchScopeOverrideByUserId",
                table: "EmailTemplates",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DispatchScopeOverride", table: "EmailTemplates");

            migrationBuilder.DropColumn(name: "DispatchScopeOverrideAtUtc", table: "EmailTemplates");

            migrationBuilder.DropColumn(name: "DispatchScopeOverrideByUserId", table: "EmailTemplates");
        }
    }
}
