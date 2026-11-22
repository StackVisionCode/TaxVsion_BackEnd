using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// D-N50: los de N meses vigentes que terminan en los próximos 7 días ya recibieron el aviso con la regla vieja
    /// (o nunca lo recibirían); se marcan para que la regla nueva no lo repita. Solo toca la columna nueva.
    /// </summary>
    public partial class BackfillDiscountEndingNotices : Migration
    {
        // sp_executesql: compilación diferida por si un script idempotente junta este lote con el ALTER de la columna.
        public const string Sql = """
            EXEC sp_executesql N'UPDATE SubscriptionDiscounts
            SET EndingNoticeSentAtUtc = SYSUTCDATETIME()
            WHERE Duration = N''Months''
              AND EndedAtUtc IS NULL
              AND EndingNoticeSentAtUtc IS NULL
              AND EndsAtUtc > SYSUTCDATETIME()
              AND EndsAtUtc <= DATEADD(day, 7, SYSUTCDATETIME());';
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin vuelta atrás: no se distingue esta marca de un aviso real.
        }
    }
}
