using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropTenantEmailProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantEmailProviders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantEmailProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BulkRateLimitPerMinute = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    FromAddressDefault = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    FromDisplayNameDefault = table.Column<string>(
                        type: "nvarchar(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    Host = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PasswordCipher = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Port = table.Column<int>(type: "int", nullable: true),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProviderType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RateLimitPerMinute = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UseTls = table.Column<bool>(type: "bit", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantEmailProviders", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantEmailProviders_TenantId_ProviderCode",
                table: "TenantEmailProviders",
                columns: new[] { "TenantId", "ProviderCode" },
                unique: true
            );
        }
    }
}
