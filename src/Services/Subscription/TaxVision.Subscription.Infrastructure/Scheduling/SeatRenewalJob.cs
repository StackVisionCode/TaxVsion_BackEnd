using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>Publica el intent de cobro por cada seat que llega a su NextRenewalAtUtc.
/// Completamente independiente de TenantSubscriptionRenewalJob — un seat puede renovarse
/// aunque la suscripción base no venza ese mismo día, y viceversa.</summary>
public sealed class SeatRenewalJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<SeatRenewalJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    private const string BaseEndedReason = "Base subscription ended";

    protected override string JobName => "seat-renewal";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var seats = services.GetRequiredService<ISubscriptionSeatRepository>();
        var logger = services.GetRequiredService<ILogger<SeatRenewalJob>>();

        var nowUtc = DateTime.UtcNow;
        var due = await seats.GetDueForRenewalAsync(nowUtc, BatchSize, ct);
        if (due.Count == 0)
            return;

        // El job es el origen de la traza: un id por pasada, para seguir junto todo lo que publique.
        var correlationId = Guid.NewGuid().ToString("N");
        await RenewAsync([.. due.Select(seat => (seat.Id, seat.TenantId))], nowUtc, correlationId, ct);
        logger.LogInformation("SeatRenewalJob processed {Count} due seat(s).", due.Count);
    }

    internal Task<JobBatchSummary> RenewAsync(
        IReadOnlyList<(Guid SeatId, Guid TenantId)> seats,
        DateTime nowUtc,
        string correlationId,
        CancellationToken ct
    ) => RunUnitsAsync(seats, (seat, unit, token) => RenewOneAsync(unit, seat, nowUtc, correlationId, token), ct);

    // La renovación en Processing y su SeatRenewalDue (el cobro) se confirman juntos o no se confirma nada.
    private static async Task<bool> RenewOneAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        (Guid SeatId, Guid TenantId) item,
        DateTime nowUtc,
        string correlationId,
        CancellationToken ct
    )
    {
        unit.UseTenant(item.TenantId);
        var seat = await unit
            .Services.GetRequiredService<ISubscriptionSeatRepository>()
            .GetByIdAsync(item.SeatId, item.TenantId, ct);
        if (seat is null)
            return false;

        // C5 — mismo criterio que los add-ons. El tenant puede desactivar la pausa (seguir pagando
        // asientos con la base suspendida), pero con la base terminada el asiento se cancela igual.
        var subscription = await unit
            .Services.GetRequiredService<ISubscriptionRepository>()
            .GetByTenantIdAsync(item.TenantId, ct);
        if (subscription is not null)
        {
            var decision = ExtraBilling.Decide(subscription.Status);
            if (decision == ExtraBillingDecision.Cancel)
                return seat.CancelActive(BaseEndedReason, actorUserId: Guid.Empty, nowUtc).IsSuccess;

            if (decision == ExtraBillingDecision.Pause)
            {
                var settings = await unit
                    .Services.GetRequiredService<ISubscriptionTenantSettingsRepository>()
                    .GetByTenantIdAsync(item.TenantId, ct);
                if (settings is null || settings.PauseSeatRenewalsWhenBaseSuspended)
                    return false;
            }
        }

        // Un cambio de ciclo en curso o agendado lo aplica la base primero; el asiento renueva después.
        if (subscription?.HoldsExtraRenewals(seat.CurrentPeriodEndUtc!.Value) == true)
            return false;

        var idempotencyKey = IdempotencyKeyFactory.SeatRenewal(seat.Id, seat.CurrentPeriodEndUtc!.Value);
        var result = seat.BeginRenewal(idempotencyKey, actorUserId: Guid.Empty, nowUtc);
        if (result.IsFailure)
        {
            unit.Services.GetRequiredService<ILogger<SeatRenewalJob>>()
                .LogWarning("Could not begin renewal for seat {SeatId}: {Code}.", seat.Id, result.Error.Code);
            return false;
        }

        await unit.PublishAsync(
            new SeatRenewalDueIntegrationEvent
            {
                TenantId = seat.TenantId,
                CorrelationId = correlationId,
                SeatId = seat.Id,
                PeriodStartUtc = seat.CurrentPeriodEndUtc.Value,
                PeriodEndUtc = seat.BillingCycle.CalculateNext(seat.CurrentPeriodEndUtc.Value),
                IdempotencyKey = idempotencyKey,
                AmountCents = (long)Math.Round(seat.UnitPrice.Amount * 100m, MidpointRounding.AwayFromZero),
                Currency = seat.UnitPrice.Currency,
            }
        );
        return true;
    }
}
