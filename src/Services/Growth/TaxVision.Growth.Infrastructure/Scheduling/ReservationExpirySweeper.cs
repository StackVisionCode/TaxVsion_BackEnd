using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Reservations.ExpireReservation;
using Wolverine;

namespace TaxVision.Growth.Infrastructure.Scheduling;

/// <summary>
/// Red de seguridad para reservas de código abandonadas. El flujo de onboarding reserva un código
/// (hold de disponibilidad) antes de pagar; si el comprador nunca paga y nadie llama Cancel, la reserva
/// quedaría <c>Active</c> para siempre y quemaría un código de un solo uso. Este barrido periódico
/// encuentra reservas <c>Active</c> vencidas (TTL = 24h) y despacha <see cref="ExpireReservationCommand"/>
/// por cada una — con su TenantId en el envelope para que el handler corra con el contexto de tenant
/// correcto (vía GrowthLocalCommandTenantMiddleware) y libere el hold.
///
/// Single-instance en dev. En multi-réplica, dos barridos concurrentes son inocuos: el guard del dominio
/// (<c>CodeReservation.Expire</c> solo transiciona Active→Expired) + la concurrencia optimista (RowVersion)
/// hacen que el segundo caiga en no-op o conflicto reintentable, nunca en doble liberación.
/// </summary>
public sealed class ReservationExpirySweeper(
    IServiceScopeFactory scopeFactory,
    ILogger<ReservationExpirySweeper> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reservation expiry sweep failed; will retry next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var reservations = scope.ServiceProvider.GetRequiredService<ICodeReservationRepository>();

        var expired = await reservations.GetActiveExpiredAsync(DateTime.UtcNow, BatchSize, ct);
        if (expired.Count == 0)
            return;

        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var swept = 0;
        foreach (var reservation in expired)
        {
            // El middleware de Growth restaura el TenantContext desde envelope.TenantId (= bus.TenantId).
            bus.TenantId = reservation.TenantId.ToString();
            // Reusa el comando/handler de expiry existente (idempotente por (tenant, reserva, key)); la key
            // estable por reserva deduplica barridos concurrentes. Este es el ÚNICO invocador del expiry —
            // el endpoint existía pero nadie lo llamaba, dejando reservas abandonadas Active para siempre.
            var result = await bus.InvokeAsync<Result<ExpireReservationResponse>>(
                new ExpireReservationCommand(
                    reservation.TenantId,
                    reservation.ReservationId,
                    $"expire-sweep:{reservation.ReservationId:N}"
                ),
                ct
            );
            if (result.IsSuccess)
                swept++;
            else
                logger.LogWarning(
                    "Failed to expire reservation {ReservationId} (tenant {TenantId}): {Error}",
                    reservation.ReservationId,
                    reservation.TenantId,
                    result.Error.Code
                );
        }

        logger.LogInformation("Reservation expiry sweep expired {Swept}/{Total} reservation(s).", swept, expired.Count);
    }
}
