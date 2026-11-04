using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowOffboardedStaffEmailReuse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D-8: el staff retirado sale del índice único; su fila queda como registro histórico.
            migrationBuilder.DropIndex(name: "IX_Users_TenantId_Email_Staff", table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email_Staff",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true,
                filter: "[ActorType] <> N'CustomerPortal' AND [Status] <> N'Offboarded'"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Falla si ya hay un email reusado (retirado + cuenta nueva): resolverlo antes de revertir.
            migrationBuilder.DropIndex(name: "IX_Users_TenantId_Email_Staff", table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email_Staff",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true,
                filter: "[ActorType] <> N'CustomerPortal'"
            );
        }
    }
}
