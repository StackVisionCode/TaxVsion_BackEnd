using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Infrastructure.Persistence;

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

    internal enum ExpirationCause
    {
        SuspensionTimeout,
        CancellationEnded,
    }

    protected override string JobName => "subscription-expiration";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var logger = services.GetRequiredService<ILogger<SubscriptionExpirationJob>>();

        var nowUtc = DateTime.UtcNow;
        var suspendedTimedOut = await subscriptions.GetSuspendedBeforeAsync(nowUtc - SuspensionTimeout, BatchSize, ct);
        var cancelledPastPeriod = await subscriptions.GetCancelledPastPeriodEndAsync(nowUtc, BatchSize, ct);

        List<(Guid TenantId, ExpirationCause Cause)> candidates =
        [
            .. suspendedTimedOut.Select(s => (s.TenantId, ExpirationCause.SuspensionTimeout)),
            .. cancelledPastPeriod.Select(s => (s.TenantId, ExpirationCause.CancellationEnded)),
        ];
        if (candidates.Count == 0)
            return;

        var summary = await ExpireAsync(candidates, nowUtc, ct);
        if (summary.Committed > 0)
            logger.LogInformation("SubscriptionExpirationJob expired {Count} subscription(s).", summary.Committed);
    }

    internal Task<JobBatchSummary> ExpireAsync(
        IReadOnlyList<(Guid TenantId, ExpirationCause Cause)> candidates,
        DateTime nowUtc,
        CancellationToken ct
    ) => RunUnitsAsync(candidates, (candidate, unit, token) => ExpireOneAsync(unit, candidate, nowUtc, token), ct);

    // Estado, aviso (evento de dominio) y recálculo (cola local durable) se confirman juntos o no se confirma nada.
    private static async Task<bool> ExpireOneAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        (Guid TenantId, ExpirationCause Cause) candidate,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        unit.UseTenant(candidate.TenantId);
        var subscription = await unit
            .Services.GetRequiredService<ISubscriptionRepository>()
            .GetByTenantIdAsync(candidate.TenantId, ct);
        if (subscription is null)
            return false;

        var result =
            candidate.Cause == ExpirationCause.SuspensionTimeout
                ? subscription.ExpireAfterSuspensionTimeout(Guid.Empty, nowUtc)
                : subscription.ExpireAfterCancellationPeriodEnded(Guid.Empty, nowUtc);
        if (result.IsFailure)
            return false;

        await unit.PublishAsync(new RecalculateEntitlementsCommand(candidate.TenantId));
        return true;
    }
}
