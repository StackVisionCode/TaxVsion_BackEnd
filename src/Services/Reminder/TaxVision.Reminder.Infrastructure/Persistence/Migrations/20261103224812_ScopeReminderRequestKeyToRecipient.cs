using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Reminder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeReminderRequestKeyToRecipient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La idempotencia pasa a ser por destinatario: la misma clave para otro usuario (objetivo
            // reasignado) crea su aviso. Las filas actuales ya cumplen el índice nuevo (es más laxo).
            migrationBuilder.DropIndex(name: "UX_Reminders_TenantId_RequestKey", table: "Reminders");

            migrationBuilder.CreateIndex(
                name: "UX_Reminders_TenantId_UserId_RequestKey",
                table: "Reminders",
                columns: new[] { "TenantId", "UserId", "RequestKey" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Falla si ya hay la misma clave en dos destinatarios del tenant: depurarlas antes de bajar.
            migrationBuilder.DropIndex(name: "UX_Reminders_TenantId_UserId_RequestKey", table: "Reminders");

            migrationBuilder.CreateIndex(
                name: "UX_Reminders_TenantId_RequestKey",
                table: "Reminders",
                columns: new[] { "TenantId", "RequestKey" },
                unique: true
            );
        }
    }
}
