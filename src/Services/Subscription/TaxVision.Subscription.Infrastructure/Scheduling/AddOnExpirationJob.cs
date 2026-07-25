using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using Wolverine;

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
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<AddOnExpirationJob>>();

        var nowUtc = DateTime.UtcNow;
        var expiredCount = 0;

        var suspendedTimedOut = await tenantAddOns.GetSuspendedBeforeAsync(nowUtc - SuspensionTimeout, BatchSize, ct);
        foreach (var addOn in suspendedTimedOut)
            expiredCount += await TryExpireAsync(
                addOn.ExpireAfterSuspensionTimeout(Guid.Empty, nowUtc),
                addOn.TenantId,
                unitOfWork,
                bus,
                logger,
                ct
            );

        var cancelledPastPeriod = await tenantAddOns.GetCancelledPastPeriodEndAsync(nowUtc, BatchSize, ct);
        foreach (var addOn in cancelledPastPeriod)
            expiredCount += await TryExpireAsync(
                addOn.ExpireAfterCancellationPeriodEnded(Guid.Empty, nowUtc),
                addOn.TenantId,
                unitOfWork,
                bus,
                logger,
                ct
            );

        if (expiredCount > 0)
            logger.LogInformation("AddOnExpirationJob expired {Count} add-on(s).", expiredCount);
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

        // RBAC Fase 5 — RecalculateEntitlementsSafelyAsync despacha vía bus.InvokeAsync a un
        // scope Wolverine nuevo; sin este stamp LocalCommandTenantMiddleware no tiene tenant
        // que restaurar y el filtro fail-closed de SubscriptionDbContext bloquearía el handler.
        bus.TenantId = tenantId.ToString();
        await bus.RecalculateEntitlementsSafelyAsync(tenantId, logger, ct);
        return 1;
    }
}
