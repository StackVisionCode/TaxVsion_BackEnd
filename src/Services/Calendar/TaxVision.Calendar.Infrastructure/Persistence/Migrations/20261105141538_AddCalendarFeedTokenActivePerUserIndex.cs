using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Calendar.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Un solo feed token activo por usuario. Requiere la consulta previa de duplicados (CAL-4):
    /// si un usuario tiene dos activos, la creación del índice falla.
    /// </summary>
    public partial class AddCalendarFeedTokenActivePerUserIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_CalendarFeedTokens_ActivePerUser",
                table: "CalendarFeedTokens",
                columns: new[] { "TenantId", "UserId" },
                unique: true,
                filter: "[RevokedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CalendarFeedTokens_ActivePerUser",
                table: "CalendarFeedTokens");
        }
    }
}
