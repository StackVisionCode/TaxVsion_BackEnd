using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using BuildingBlocks.Results;
using Wolverine;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Expira los trials que llegaron a TrialEndsAtUtc sin convertirse a Active. La conversión
/// automática al terminar el trial (cobro real) queda para cuando exista Billing (Fase 5);
/// hasta entonces, un trial no convertido expira sin más.
/// </summary>
public sealed class TrialExpirationJob(IServiceScopeFactory scopeFactory, IDistributedLockFactory lockFactory, ILogger<TrialExpirationJob> logger)
    : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    protected override string JobName => "trial-expiration";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<TrialExpirationJob>>();

        var nowUtc = DateTime.UtcNow;
        var expired = await subscriptions.GetExpiredTrialsAsync(nowUtc, BatchSize, ct);

        foreach (var subscription in expired)
        {
            var result = subscription.ExpireTrialWithoutConversion(actorUserId: Guid.Empty, nowUtc);
            if (result.IsFailure)
            {
                logger.LogWarning("Could not expire trial for subscription {SubscriptionId}: {Code}.", subscription.Id, result.Error.Code);
                continue;
            }

            await unitOfWork.SaveChangesAsync(ct);
            await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(subscription.TenantId), ct);
        }

        if (expired.Count > 0)
            logger.LogInformation("TrialExpirationJob expired {Count} trial(s).", expired.Count);
    }
}
