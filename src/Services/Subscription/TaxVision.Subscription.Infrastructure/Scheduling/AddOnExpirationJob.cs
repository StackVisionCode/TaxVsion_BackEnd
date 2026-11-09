using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Expira definitivamente add-ons que llevan suspendidos más de 30 días, o cuya
/// cancelación ya pasó el fin del período pagado. Independiente de los otros jobs de
/// expiración — no forma parte del catálogo original del diseño (§45 Fase 4) pero cierra
/// el ciclo de vida de TenantAddOn.ExpireAfterSuspensionTimeout/
/// ExpireAfterCancellationPeriodEnded, que de otro modo quedarían sin invocador.
/// </summary>
public sealed class AddOnExpirationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<AddOnExpirationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(6), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;
    private static readonly TimeSpan SuspensionTimeout = TimeSpan.FromDays(30);

    protected override string JobName => "addon-expiration";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var tenantAddOns = services.GetRequiredService<ITenantAddOnRepository>();
        var logger = services.GetRequiredService<ILogger<AddOnExpirationJob>>();

        var nowUtc = DateTime.UtcNow;
        var suspendedTimedOut = await tenantAddOns.GetSuspendedBeforeAsync(nowUtc - SuspensionTimeout, BatchSize, ct);
        var cancelledPastPeriod = await tenantAddOns.GetCancelledPastPeriodEndAsync(nowUtc, BatchSize, ct);

        List<(Guid TenantAddOnId, Guid TenantId, bool SuspensionTimedOut)> candidates =
        [
            .. suspendedTimedOut.Select(a => (a.Id, a.TenantId, true)),
            .. cancelledPastPeriod.Select(a => (a.Id, a.TenantId, false)),
        ];
        if (candidates.Count == 0)
            return;

        var summary = await RunUnitsAsync(
            candidates,
            (candidate, unit, token) => ExpireOneAsync(unit, candidate, nowUtc, token),
            ct
        );
        if (summary.Committed > 0)
            logger.LogInformation("AddOnExpirationJob expired {Count} add-on(s).", summary.Committed);
    }

    // Expiración y recálculo (cola local durable) en la misma unidad.
    private static async Task<bool> ExpireOneAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        (Guid TenantAddOnId, Guid TenantId, bool SuspensionTimedOut) candidate,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        unit.UseTenant(candidate.TenantId);
        var addOn = await unit
            .Services.GetRequiredService<ITenantAddOnRepository>()
            .GetByIdAsync(candidate.TenantAddOnId, candidate.TenantId, ct);
        if (addOn is null)
            return false;

        var result = candidate.SuspensionTimedOut
            ? addOn.ExpireAfterSuspensionTimeout(Guid.Empty, nowUtc)
            : addOn.ExpireAfterCancellationPeriodEnded(Guid.Empty, nowUtc);
        if (result.IsFailure)
            return false;

        await unit.PublishAsync(new RecalculateEntitlementsCommand(candidate.TenantId));
        unit.Services.GetRequiredService<ISubscriptionMetrics>().RecordAddOnExpired(addOn.AddOnCode);
        return true;
    }
}
