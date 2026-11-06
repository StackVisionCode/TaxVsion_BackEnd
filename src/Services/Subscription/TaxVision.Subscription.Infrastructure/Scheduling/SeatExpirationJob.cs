using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Expira definitivamente seats que llevan suspendidos más de 30 días, o cuya cancelación
/// ya pasó el fin del período pagado. Independiente de SubscriptionExpirationJob.
/// </summary>
public sealed class SeatExpirationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<SeatExpirationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(6), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;
    private static readonly TimeSpan SuspensionTimeout = TimeSpan.FromDays(30);

    protected override string JobName => "seat-expiration";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var seats = services.GetRequiredService<ISubscriptionSeatRepository>();
        var logger = services.GetRequiredService<ILogger<SeatExpirationJob>>();

        var nowUtc = DateTime.UtcNow;
        var suspendedTimedOut = await seats.GetSuspendedBeforeAsync(nowUtc - SuspensionTimeout, BatchSize, ct);
        var cancelledPastPeriod = await seats.GetCancelledPastPeriodEndAsync(nowUtc, BatchSize, ct);

        List<(Guid SeatId, Guid TenantId, bool SuspensionTimedOut)> candidates =
        [
            .. suspendedTimedOut.Select(s => (s.Id, s.TenantId, true)),
            .. cancelledPastPeriod.Select(s => (s.Id, s.TenantId, false)),
        ];
        if (candidates.Count == 0)
            return;

        var summary = await RunUnitsAsync(
            candidates,
            (candidate, unit, token) => ExpireOneAsync(unit, candidate, nowUtc, token),
            ct
        );
        if (summary.Committed > 0)
            logger.LogInformation("SeatExpirationJob expired {Count} seat(s).", summary.Committed);
    }

    // Expiración y recálculo (cola local durable) en la misma unidad.
    private static async Task<bool> ExpireOneAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        (Guid SeatId, Guid TenantId, bool SuspensionTimedOut) candidate,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        unit.UseTenant(candidate.TenantId);
        var seat = await unit
            .Services.GetRequiredService<ISubscriptionSeatRepository>()
            .GetByIdAsync(candidate.SeatId, candidate.TenantId, ct);
        if (seat is null)
            return false;

        var result = candidate.SuspensionTimedOut
            ? seat.ExpireAfterSuspensionTimeout(Guid.Empty, nowUtc)
            : seat.ExpireAfterCancellationPeriodEnded(Guid.Empty, nowUtc);
        if (result.IsFailure)
            return false;

        await unit.PublishAsync(new RecalculateEntitlementsCommand(candidate.TenantId));
        return true;
    }
}
