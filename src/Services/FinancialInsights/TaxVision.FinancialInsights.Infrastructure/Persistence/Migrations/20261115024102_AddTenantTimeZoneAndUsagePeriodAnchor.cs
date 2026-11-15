using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.FinancialInsights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantTimeZoneAndUsagePeriodAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UsagePeriodAnchorUtc",
                table: "TenantEntitlementsProjections",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "TenantTimeZoneProjections",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceVersion = table.Column<long>(type: "bigint", nullable: true),
                    SourceUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantTimeZoneProjections", x => x.TenantId);
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantTimeZoneProjections");

            migrationBuilder.DropColumn(name: "UsagePeriodAnchorUtc", table: "TenantEntitlementsProjections");
        }
    }
}
