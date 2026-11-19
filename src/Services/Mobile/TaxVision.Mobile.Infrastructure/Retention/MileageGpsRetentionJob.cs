using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Infrastructure.Persistence;

namespace TaxVision.Mobile.Infrastructure.Retention;

internal sealed class MileageGpsRetentionJob(IServiceScopeFactory scopes, ILogger<MileageGpsRetentionJob> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await PurgeAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Mileage GPS retention purge failed; retrying next cycle."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MobileDbContext>();
        var policies = await db.MileageRetentionPolicies.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.RetentionDays != null).ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var policy in policies)
        {
            var cutoff = now.AddDays(-policy.RetentionDays!.Value);
            var candidates = await db.Trips.IgnoreQueryFilters()
                .Where(t => t.TenantId == policy.TenantId && t.Status == TripStatus.Completed &&
                    t.DeletedAtUtc == null && t.CoordinatesPurgedAtUtc == null && t.EndedAtUtc <= cutoff)
                .OrderBy(t => t.EndedAtUtc).Take(BatchSize).ToListAsync(ct);
            foreach (var trip in candidates) trip.PurgeCoordinates(now);
            if (candidates.Count != 0) await db.SaveChangesAsync(ct);
        }
    }
}
