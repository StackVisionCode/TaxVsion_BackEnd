using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using Wolverine;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>Publica el intent de cobro por cada add-on que llega a su NextRenewalAtUtc.
/// Independiente de la suscripción base y de los seats.</summary>
public sealed class AddOnRenewalJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<AddOnRenewalJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    protected override string JobName => "addon-renewal";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var tenantAddOns = services.GetRequiredService<ITenantAddOnRepository>();
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var correlation = services.GetRequiredService<ICorrelationContext>();
        // El job es el origen de la traza: un id por pasada, para seguir junto todo lo que publique.
        using var correlationScope = correlation.Push(Guid.NewGuid().ToString("N"));
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var metrics = services.GetRequiredService<ISubscriptionMetrics>();
        var logger = services.GetRequiredService<ILogger<AddOnRenewalJob>>();

        var nowUtc = DateTime.UtcNow;
        var due = await tenantAddOns.GetDueForRenewalAsync(nowUtc, BatchSize, ct);

        foreach (var addOn in due)
        {
            // Co-terminación: la renovación llega hasta el próximo aniversario de la base, no por el
            // ciclo propio del add-on, para que no se desfase del plan.
            var subscription = await subscriptions.GetByTenantIdAsync(addOn.TenantId, ct);
            if (subscription is null)
            {
                logger.LogWarning("Add-on {TenantAddOnId} has no base subscription; skipping renewal.", addOn.Id);
                continue;
            }

            var newPeriodEndUtc = subscription.NextCoTermEnd(addOn.CurrentPeriodEndUtc);

            var idempotencyKey = IdempotencyKeyFactory.AddOnRenewal(addOn.Id, addOn.CurrentPeriodEndUtc);
            var result = addOn.BeginRenewal(idempotencyKey, newPeriodEndUtc, actorUserId: Guid.Empty, nowUtc);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not begin renewal for add-on {TenantAddOnId}: {Code}.",
                    addOn.Id,
                    result.Error.Code
                );
                continue;
            }

            await unitOfWork.SaveChangesAsync(ct);

            var amountCents = (long)Math.Round(addOn.UnitPrice.Amount * 100m, MidpointRounding.AwayFromZero);
            await bus.PublishAsync(
                new AddOnRenewalDueIntegrationEvent
                {
                    TenantId = addOn.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    TenantAddOnId = addOn.Id,
                    AddOnCode = addOn.AddOnCode,
                    PeriodStartUtc = addOn.CurrentPeriodEndUtc,
                    PeriodEndUtc = newPeriodEndUtc,
                    IdempotencyKey = idempotencyKey,
                    AmountCents = amountCents,
                    Currency = addOn.UnitPrice.Currency,
                }
            );
            metrics.RecordAddOnBilled(addOn.AddOnCode, amountCents);
        }

        if (due.Count > 0)
            logger.LogInformation("AddOnRenewalJob processed {Count} due add-on(s).", due.Count);
    }
}
