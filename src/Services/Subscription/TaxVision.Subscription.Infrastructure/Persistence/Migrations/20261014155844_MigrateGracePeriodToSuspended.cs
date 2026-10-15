using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Mueve de GracePeriod a Suspended en subscripciones, seats y add-ons. El flow ya no da ventana con
    /// acceso vivo tras un impago: PastDue va directo a Suspended. Las filas que quedaron en GracePeriod
    /// pasan a Suspended con SuspendedAtUtc = GracePeriodEndsAtUtc (o ahora, si está vacío).
    /// </summary>
    public partial class MigrateGracePeriodToSuspended : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [TenantSubscriptions]
                SET
                    [Status] = N'Suspended',
                    [SuspendedAtUtc] = COALESCE([SuspendedAtUtc], [GracePeriodEndsAtUtc], SYSUTCDATETIME()),
                    [SuspensionReason] = COALESCE([SuspensionReason], N'GraceExpired')
                WHERE [Status] = N'GracePeriod';
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE [SubscriptionSeats]
                SET
                    [Status] = N'Suspended',
                    [SuspendedAtUtc] = COALESCE([SuspendedAtUtc], [GracePeriodEndsAtUtc], SYSUTCDATETIME()),
                    [SuspensionReason] = COALESCE([SuspensionReason], N'GraceExpired')
                WHERE [Status] = N'GracePeriod';
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE [TenantAddOns]
                SET
                    [Status] = N'Suspended',
                    [SuspendedAtUtc] = COALESCE([SuspendedAtUtc], [GracePeriodEndsAtUtc], SYSUTCDATETIME()),
                    [SuspensionReason] = COALESCE([SuspensionReason], N'GraceExpired')
                WHERE [Status] = N'GracePeriod';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No se puede volver atrás: no sabemos cuáles de las filas Suspended venían de GracePeriod.
            // Deshacer requeriría una tabla de auditoría que no existe.
        }
    }
}
