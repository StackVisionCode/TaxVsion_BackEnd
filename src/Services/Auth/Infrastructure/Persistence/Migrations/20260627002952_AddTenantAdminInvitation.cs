using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantAdminInvitation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminEmail",
                table: "Tenants",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdminInvitationConsumedAtUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminInvitationTokenHash",
                table: "Tenants",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdminUserId",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_AdminEmail",
                table: "Tenants",
                column: "AdminEmail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_AdminEmail",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AdminEmail",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AdminInvitationConsumedAtUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AdminInvitationTokenHash",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AdminUserId",
                table: "Tenants");
        }
    }
}
