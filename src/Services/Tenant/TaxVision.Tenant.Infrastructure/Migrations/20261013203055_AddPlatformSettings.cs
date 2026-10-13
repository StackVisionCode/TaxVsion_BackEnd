using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    BaseDomain = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    ApiHost = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    SupportEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    BillingEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Id);
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlatformSettings");
        }
    }
}
