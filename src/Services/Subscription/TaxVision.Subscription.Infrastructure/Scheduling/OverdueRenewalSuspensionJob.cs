using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Red de seguridad: si el período venció hace más de 2 días y el cobro nunca salió, la suscripción se
/// suspende. Sin esto una renovación perdida dejaba la oficina activa sin pagar para siempre.
/// </summary>
public sealed class OverdueRenewalSuspensionJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<OverdueRenewalSuspensionJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(10))
{
    private const int BatchSize = 200;

    /// <summary>Más que la escalera de reintentos de PaymentApp (1 h + 6 h + 24 h).</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromDays(2);

    protected override string JobName => "overdue-renewal-suspension";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var logger = services.GetRequiredService<ILogger<OverdueRenewalSuspensionJob>>();

        var nowUtc = DateTime.UtcNow;
        var overdue = await subscriptions.GetActiveWithPeriodEndedBeforeAsync(nowUtc - Tolerance, BatchSize, ct);
        if (overdue.Count == 0)
            return;

        var summary = await SuspendAsync([.. overdue.Select(subscription => subscription.TenantId)], nowUtc, ct);
        if (summary.Committed > 0)
            logger.LogInformation("OverdueRenewalSuspensionJob suspended {Count} subscription(s).", summary.Committed);
    }

    internal Task<JobBatchSummary> SuspendAsync(IReadOnlyList<Guid> tenantIds, DateTime nowUtc, CancellationToken ct) =>
        RunUnitsAsync(tenantIds, (tenantId, unit, token) => SuspendOneAsync(unit, tenantId, nowUtc, token), ct);

    // Suspensión, aviso RenewalOverdue (evento de dominio), recálculo y cobros superados salen en la misma unidad.
    private static async Task<bool> SuspendOneAsync(
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

        var result = subscription.SuspendBecauseRenewalOverdue(Tolerance, Guid.Empty, nowUtc);
        if (result.IsFailure)
            return false;

        await unit.PublishAsync(new RecalculateEntitlementsCommand(tenantId));
        await unit.Outbox.PublishRenewalChargesSupersededAsync(tenantId, result.Value, string.Empty);

        unit.Services.GetRequiredService<ILogger<OverdueRenewalSuspensionJob>>()
            .LogWarning(
                "Suspended subscription {SubscriptionId} of tenant {TenantId}: period ended {PeriodEndUtc} and was never renewed.",
                subscription.Id,
                subscription.TenantId,
                subscription.CurrentPeriodEndUtc
            );
        return true;
    }
}
