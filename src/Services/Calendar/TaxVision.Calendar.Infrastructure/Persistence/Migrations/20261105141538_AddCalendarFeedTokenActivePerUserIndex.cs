using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Calendar.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Un solo feed token activo por usuario. Si un usuario tiene varios activos, queda el más nuevo y los demás
    /// se revocan (CAL-4); no se borra ninguno.
    /// </summary>
    public partial class AddCalendarFeedTokenActivePerUserIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT RevokedAtUtc, ROW_NUMBER() OVER (
                        PARTITION BY TenantId, UserId ORDER BY CreatedAtUtc DESC, Id DESC) AS rn
                    FROM CalendarFeedTokens WHERE RevokedAtUtc IS NULL
                )
                UPDATE ranked SET RevokedAtUtc = SYSUTCDATETIME() WHERE rn > 1;
                """
            );

            migrationBuilder.CreateIndex(
                name: "UX_CalendarFeedTokens_ActivePerUser",
                table: "CalendarFeedTokens",
                columns: new[] { "TenantId", "UserId" },
                unique: true,
                filter: "[RevokedAtUtc] IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_CalendarFeedTokens_ActivePerUser", table: "CalendarFeedTokens");
        }
    }
}
