using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using Wolverine;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Expira definitivamente seats que llevan suspendidos más de 30 días, o cuya cancelación
/// ya pasó el fin del período pagado. Independiente de SubscriptionExpirationJob.
/// </summary>
public sealed class SeatExpirationJob(IServiceScopeFactory scopeFactory, IDistributedLockFactory lockFactory, ILogger<SeatExpirationJob> logger)
    : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(6), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;
    private static readonly TimeSpan SuspensionTimeout = TimeSpan.FromDays(30);

    protected override string JobName => "seat-expiration";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var seats = services.GetRequiredService<ISubscriptionSeatRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<SeatExpirationJob>>();

        var nowUtc = DateTime.UtcNow;
        var expiredCount = 0;

        var suspendedTimedOut = await seats.GetSuspendedBeforeAsync(nowUtc - SuspensionTimeout, BatchSize, ct);
        foreach (var seat in suspendedTimedOut)
            expiredCount += await TryExpireAsync(seat.ExpireAfterSuspensionTimeout(Guid.Empty, nowUtc), seat.TenantId, unitOfWork, bus, ct);

        var cancelledPastPeriod = await seats.GetCancelledPastPeriodEndAsync(nowUtc, BatchSize, ct);
        foreach (var seat in cancelledPastPeriod)
            expiredCount += await TryExpireAsync(seat.ExpireAfterCancellationPeriodEnded(Guid.Empty, nowUtc), seat.TenantId, unitOfWork, bus, ct);

        if (expiredCount > 0)
            logger.LogInformation("SeatExpirationJob expired {Count} seat(s).", expiredCount);
    }

    private static async Task<int> TryExpireAsync(Result result, Guid tenantId, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        if (result.IsFailure)
            return 0;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(tenantId), ct);
        return 1;
    }
}
