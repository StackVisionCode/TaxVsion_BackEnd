using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Expira los trials que llegaron a TrialEndsAtUtc sin convertirse a Active. La conversión
/// automática al terminar el trial (cobro real) queda para cuando exista Billing (Fase 5);
/// hasta entonces, un trial no convertido expira sin más.
/// </summary>
public sealed class TrialExpirationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<TrialExpirationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    protected override string JobName => "trial-expiration";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var logger = services.GetRequiredService<ILogger<TrialExpirationJob>>();

        var nowUtc = DateTime.UtcNow;
        var expired = await subscriptions.GetExpiredTrialsAsync(nowUtc, BatchSize, ct);
        if (expired.Count == 0)
            return;

        var summary = await ExpireAsync([.. expired.Select(subscription => subscription.TenantId)], nowUtc, ct);
        if (summary.Committed > 0)
            logger.LogInformation("TrialExpirationJob expired {Count} trial(s).", summary.Committed);
    }

    internal Task<JobBatchSummary> ExpireAsync(IReadOnlyList<Guid> tenantIds, DateTime nowUtc, CancellationToken ct) =>
        RunUnitsAsync(tenantIds, (tenantId, unit, token) => ExpireOneAsync(unit, tenantId, nowUtc, token), ct);

    // El aviso TrialEnded sale del evento de dominio; el recálculo va por la cola local durable, en la misma unidad.
    private static async Task<bool> ExpireOneAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        Guid tenantId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        unit.UseTenant(tenantId);
        var subscription = await unit
            .Services.GetRequiredService<ISubscriptionRepository>()
            .GetByTenantIdAsync(tenantId, ct);
        if (subscription is null)
            return false;

        var result = subscription.ExpireTrialWithoutConversion(actorUserId: Guid.Empty, nowUtc);
        if (result.IsFailure)
        {
            unit.Services.GetRequiredService<ILogger<TrialExpirationJob>>()
                .LogWarning(
                    "Could not expire trial for subscription {SubscriptionId}: {Code}.",
                    subscription.Id,
                    result.Error.Code
                );
            return false;
        }

        await unit.PublishAsync(new RecalculateEntitlementsCommand(tenantId));
        return true;
    }
}
