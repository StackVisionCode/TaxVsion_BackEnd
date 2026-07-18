using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantOAuthAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantOAuthAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FromAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DisconnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantOAuthAccounts", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantOAuthAccounts_TenantId_AccountId",
                table: "TenantOAuthAccounts",
                columns: new[] { "TenantId", "AccountId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantOAuthAccounts_TenantId_IsActive_ConnectedAtUtc",
                table: "TenantOAuthAccounts",
                columns: new[] { "TenantId", "IsActive", "ConnectedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantOAuthAccounts");
        }
    }
}
