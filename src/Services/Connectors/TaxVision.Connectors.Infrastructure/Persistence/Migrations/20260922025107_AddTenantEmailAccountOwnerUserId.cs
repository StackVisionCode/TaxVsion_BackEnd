using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Connectors.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantEmailAccountOwnerUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "TenantEmailAccounts",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantEmailAccounts_TenantId_OwnerUserId",
                table: "TenantEmailAccounts",
                columns: new[] { "TenantId", "OwnerUserId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantEmailAccounts_TenantId_OwnerUserId",
                table: "TenantEmailAccounts"
            );

            migrationBuilder.DropColumn(name: "OwnerUserId", table: "TenantEmailAccounts");
        }
    }
}
