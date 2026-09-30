using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <summary>El email deja de ser único por oficina a secas: es único por oficina dentro de cada tipo de
    /// cuenta (Staff / Portal), así un empleado puede ser también cliente con el mismo email.</summary>
    public partial class SeparateStaffAndPortalAccountsByEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Users_TenantId_Email", table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email_Portal",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true,
                filter: "[ActorType] = N'CustomerPortal'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email_Staff",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true,
                filter: "[ActorType] <> N'CustomerPortal'"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Users_TenantId_Email_Portal", table: "Users");

            migrationBuilder.DropIndex(name: "IX_Users_TenantId_Email_Staff", table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true
            );
        }
    }
}
