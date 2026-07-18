using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantEmailProviderAndInlineAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InlineAssetsJson",
                table: "SentMessages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "RequiredProviderScope",
                table: "SentMessages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.CreateTable(
                name: "ProviderHealthStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderKind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    ConsecutiveSuccesses = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    CircuitBreakerState = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    CircuitBreakerOpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastCheckAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSuccessAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFailureAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderHealthStatuses", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantEmailProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProviderType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Host = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: true),
                    UseTls = table.Column<bool>(type: "bit", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PasswordCipher = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FromAddressDefault = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    FromDisplayNameDefault = table.Column<string>(
                        type: "nvarchar(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    RateLimitPerMinute = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantEmailProviders", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ProviderHealthStatuses_ProviderKind_TenantId_ProviderCode",
                table: "ProviderHealthStatuses",
                columns: new[] { "ProviderKind", "TenantId", "ProviderCode" },
                unique: true,
                filter: "[TenantId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantEmailProviders_TenantId_ProviderCode",
                table: "TenantEmailProviders",
                columns: new[] { "TenantId", "ProviderCode" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProviderHealthStatuses");

            migrationBuilder.DropTable(name: "TenantEmailProviders");

            migrationBuilder.DropColumn(name: "InlineAssetsJson", table: "SentMessages");

            migrationBuilder.DropColumn(name: "RequiredProviderScope", table: "SentMessages");
        }
    }
}
