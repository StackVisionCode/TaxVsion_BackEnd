using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.SlotGating.Commands;
using TaxVision.Auth.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Auth.Api.Bootstrap;

public sealed class SeatSlotProjectionBackfillJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<SeatSlotProjectionBackfillJob> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    private const int BatchSize = 50;
    private static readonly TimeSpan RefreshAge = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var cutoffUtc = DateTime.UtcNow.Subtract(RefreshAge);
        var page = 0;
        var refreshed = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            var tenantIds = await db
                .TenantPlanLimits.AsNoTracking()
                .Where(limits => limits.MaxUsers > 0)
                .OrderBy(limits => limits.Id)
                .Skip(page * BatchSize)
                .Take(BatchSize)
                .Select(limits => limits.Id)
                .ToListAsync(cancellationToken);

            if (tenantIds.Count == 0)
                break;

            var projectionFreshness = await db
                .TenantSeatSlotProjections.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(row => tenantIds.Contains(row.TenantId))
                .GroupBy(row => row.TenantId)
                .Select(group => new { TenantId = group.Key, ComputedAtUtc = group.Max(row => row.ComputedAtUtc) })
                .ToDictionaryAsync(row => row.TenantId, row => row.ComputedAtUtc, cancellationToken);

            foreach (var tenantId in tenantIds)
            {
                if (projectionFreshness.TryGetValue(tenantId, out var computedAtUtc) && computedAtUtc >= cutoffUtc)
                    continue;

                await bus.InvokeAsync(
                    new RecomputeTenantSeatSlotsCommand(tenantId, RefreshUnchanged: true),
                    cancellationToken
                );
                refreshed++;
            }

            if (tenantIds.Count < BatchSize)
                break;

            page++;
        }

        logger.LogInformation("Seat slot projection backfill refreshed {TenantCount} tenant(s).", refreshed);
    }
}
