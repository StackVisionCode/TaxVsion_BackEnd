using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

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
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var settingsRepository = services.GetRequiredService<ISubscriptionTenantSettingsRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var correlation = services.GetRequiredService<ICorrelationContext>();
        // El job es el origen de la traza: un id por pasada, para seguir junto todo lo que publique.
        using var correlationScope = correlation.Push(Guid.NewGuid().ToString("N"));
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<SeatRenewalJob>>();

        var nowUtc = DateTime.UtcNow;
        var due = await seats.GetDueForRenewalAsync(nowUtc, BatchSize, ct);

        foreach (var seat in due)
        {
            // C5 — mismo criterio que los add-ons. El tenant puede desactivar la pausa (seguir pagando
            // asientos con la base suspendida), pero con la base terminada el asiento se cancela igual.
            var subscription = await subscriptions.GetByTenantIdAsync(seat.TenantId, ct);
            if (subscription is not null)
            {
                var decision = ExtraBilling.Decide(subscription.Status);
                if (decision == ExtraBillingDecision.Cancel)
                {
                    if (seat.CancelActive(BaseEndedReason, actorUserId: Guid.Empty, nowUtc).IsSuccess)
                        await unitOfWork.SaveChangesAsync(ct);
                    continue;
                }

                if (decision == ExtraBillingDecision.Pause)
                {
                    var settings = await settingsRepository.GetByTenantIdAsync(seat.TenantId, ct);
                    if (settings is null || settings.PauseSeatRenewalsWhenBaseSuspended)
                        continue;
                }
            }

            var idempotencyKey = IdempotencyKeyFactory.SeatRenewal(seat.Id, seat.CurrentPeriodEndUtc!.Value);
            var result = seat.BeginRenewal(idempotencyKey, actorUserId: Guid.Empty, nowUtc);
            if (result.IsFailure)
            {
                logger.LogWarning("Could not begin renewal for seat {SeatId}: {Code}.", seat.Id, result.Error.Code);
                continue;
            }

            await unitOfWork.SaveChangesAsync(ct);

            await bus.PublishAsync(
                new SeatRenewalDueIntegrationEvent
                {
                    TenantId = seat.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SeatId = seat.Id,
                    PeriodStartUtc = seat.CurrentPeriodEndUtc.Value,
                    PeriodEndUtc = seat.BillingCycle.CalculateNext(seat.CurrentPeriodEndUtc.Value),
                    IdempotencyKey = idempotencyKey,
                    AmountCents = (long)Math.Round(seat.UnitPrice.Amount * 100m, MidpointRounding.AwayFromZero),
                    Currency = seat.UnitPrice.Currency,
                }
            );
        }

        if (due.Count > 0)
            logger.LogInformation("SeatRenewalJob processed {Count} due seat(s).", due.Count);
    }
}
