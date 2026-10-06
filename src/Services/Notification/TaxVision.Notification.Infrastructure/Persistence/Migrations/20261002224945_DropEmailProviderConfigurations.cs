using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropEmailProviderConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmailProviderConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailProviderConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApiKeyCipher = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ClientId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ClientSecretCipher = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FromEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Host = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    PasswordCipher = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: true),
                    ProviderType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantProviderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UseSsl = table.Column<bool>(type: "bit", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailProviderConfigurations", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailProviderConfigurations_Scope_TenantId",
                table: "EmailProviderConfigurations",
                columns: new[] { "Scope", "TenantId" },
                unique: true,
                filter: "[IsDefault] = 1"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailProviderConfigurations_TenantId_Scope",
                table: "EmailProviderConfigurations",
                columns: new[] { "TenantId", "Scope" }
            );
        }
    }
}
