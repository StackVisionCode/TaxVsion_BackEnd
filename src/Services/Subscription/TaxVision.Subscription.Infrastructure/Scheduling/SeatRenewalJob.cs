using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
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

    protected override string JobName => "seat-renewal";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var seats = services.GetRequiredService<ISubscriptionSeatRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<SeatRenewalJob>>();

        var nowUtc = DateTime.UtcNow;
        var due = await seats.GetDueForRenewalAsync(nowUtc, BatchSize, ct);

        foreach (var seat in due)
        {
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
