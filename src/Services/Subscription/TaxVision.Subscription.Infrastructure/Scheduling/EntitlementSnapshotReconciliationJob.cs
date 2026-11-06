using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>Cada cuánto corre la anti-entropía de entitlements y de a cuántos tenants encola por unidad.</summary>
public sealed class EntitlementSnapshotReconciliationOptions
{
    public const string SectionName = "Subscription:EntitlementReconciliation";

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan LockTtl { get; set; } = TimeSpan.FromMinutes(30);
    public int BatchSize { get; set; } = 200;
}

/// <summary>
/// SUB-4 — anti-entropía del snapshot de entitlements. Un recálculo perdido (proceso caído, cola local en memoria
/// antes de SUB-BB2) dejaba el snapshot viejo hasta el próximo cambio del tenant. Una vez al día, y al arrancar
/// (primer tick inmediato), encola <see cref="RecalculateEntitlementsCommand"/> por tenant con
/// <c>OnlyIfChanged</c>: si el snapshot ya está al día, el handler no sube la revisión ni avisa. Cada lote se
/// encola por el outbox en su unidad (BB-3), hacia la cola local durable (BB-2).
/// </summary>
public sealed class EntitlementSnapshotReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<EntitlementSnapshotReconciliationJob> logger,
    IOptions<EntitlementSnapshotReconciliationOptions> options
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, options.Value.Interval, options.Value.LockTtl)
{
    private static readonly SubscriptionStatus[] AllStatuses = Enum.GetValues<SubscriptionStatus>();

    private readonly int _batchSize = options.Value.BatchSize <= 0 ? 200 : options.Value.BatchSize;

    protected override string JobName => "entitlement-snapshot-reconciliation";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var queued = await EnqueueAllAsync(ct);
        if (queued > 0)
            Logger.LogInformation(
                "EntitlementSnapshotReconciliationJob queued an entitlement check for {Count} tenant(s).",
                queued
            );
    }

    /// <summary>Recorre los tenants con suscripción por keyset y encola un recálculo por tenant, un lote por unidad.</summary>
    internal async Task<int> EnqueueAllAsync(CancellationToken ct)
    {
        var queued = 0;
        var afterTenantId = Guid.Empty;

        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<Guid> tenantIds;
            await using (var scope = ScopeFactory.CreateAsyncScope())
            {
                var batch = await scope
                    .ServiceProvider.GetRequiredService<ISubscriptionRepository>()
                    .GetByStatusesAsync(AllStatuses, afterTenantId, _batchSize, ct);
                tenantIds = [.. batch.Select(subscription => subscription.TenantId)];
            }

            if (tenantIds.Count == 0)
                break;

            var summary = await RunUnitsAsync(
                [tenantIds],
                async (lot, unit, _) =>
                {
                    foreach (var tenantId in lot)
                        await unit.PublishAsync(new RecalculateEntitlementsCommand(tenantId, OnlyIfChanged: true));
                    return true;
                },
                ct
            );
            if (summary.Committed > 0)
                queued += tenantIds.Count;

            afterTenantId = tenantIds[^1];
            if (tenantIds.Count < _batchSize)
                break;
        }

        return queued;
    }
}
