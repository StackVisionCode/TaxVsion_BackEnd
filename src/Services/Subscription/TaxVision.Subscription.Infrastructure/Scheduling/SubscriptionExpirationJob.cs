using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using Wolverine;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Expira definitivamente suscripciones base que llevan suspendidas más de 30 días sin
/// que un admin reactive, o cuya cancelación ya pasó el fin del período pagado.
/// </summary>
public sealed class SubscriptionExpirationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<SubscriptionExpirationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(6), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;
    private static readonly TimeSpan SuspensionTimeout = TimeSpan.FromDays(30);

    protected override string JobName => "subscription-expiration";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<SubscriptionExpirationJob>>();

        var nowUtc = DateTime.UtcNow;
        var expiredCount = 0;

        var suspendedTimedOut = await subscriptions.GetSuspendedBeforeAsync(nowUtc - SuspensionTimeout, BatchSize, ct);
        foreach (var subscription in suspendedTimedOut)
            expiredCount += await TryExpireAsync(
                subscription.ExpireAfterSuspensionTimeout(Guid.Empty, nowUtc),
                subscription.TenantId,
                unitOfWork,
                bus,
                logger,
                ct
            );

        var cancelledPastPeriod = await subscriptions.GetCancelledPastPeriodEndAsync(nowUtc, BatchSize, ct);
        foreach (var subscription in cancelledPastPeriod)
            expiredCount += await TryExpireAsync(
                subscription.ExpireAfterCancellationPeriodEnded(Guid.Empty, nowUtc),
                subscription.TenantId,
                unitOfWork,
                bus,
                logger,
                ct
            );

        if (expiredCount > 0)
            logger.LogInformation("SubscriptionExpirationJob expired {Count} subscription(s).", expiredCount);
    }

    private static async Task<int> TryExpireAsync(
        Result result,
        Guid tenantId,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (result.IsFailure)
            return 0;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.RecalculateEntitlementsSafelyAsync(tenantId, logger, ct);
        return 1;
    }
}
