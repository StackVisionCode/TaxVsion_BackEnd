using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.ValueObjects;
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
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<AddOnRenewalJob>>();

        var nowUtc = DateTime.UtcNow;
        var due = await tenantAddOns.GetDueForRenewalAsync(nowUtc, BatchSize, ct);

        foreach (var addOn in due)
        {
            var idempotencyKey = IdempotencyKeyFactory.AddOnRenewal(addOn.Id, addOn.CurrentPeriodEndUtc);
            var result = addOn.BeginRenewal(idempotencyKey, actorUserId: Guid.Empty, nowUtc);
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

            await bus.PublishAsync(
                new AddOnRenewalDueIntegrationEvent
                {
                    TenantId = addOn.TenantId,
                    TenantAddOnId = addOn.Id,
                    AddOnCode = addOn.AddOnCode,
                    PeriodStartUtc = addOn.CurrentPeriodEndUtc,
                    PeriodEndUtc = addOn.BillingCycle.CalculateNext(addOn.CurrentPeriodEndUtc),
                    IdempotencyKey = idempotencyKey,
                }
            );
        }

        if (due.Count > 0)
            logger.LogInformation("AddOnRenewalJob processed {Count} due add-on(s).", due.Count);
    }
}
