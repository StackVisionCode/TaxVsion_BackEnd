using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>Runs tenant-scoped cloud provider backups at the saved local frequency and time.</summary>
public sealed class CloudStorageBackupWorker(IServiceScopeFactory scopes, ILogger<CloudStorageBackupWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueBackupsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cloud storage backup scheduler iteration failed.");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task RunDueBackupsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudStorageDbContext>();
        var nowUtc = DateTime.UtcNow;
        var scheduled = await db
            .StoragePolicies.IgnoreQueryFilters()
            .Where(x =>
                x.Mode == "backup" && x.BackupFrequency != null && x.BackupTime != null && x.BackupTimeZoneId != null
            )
            .Select(x => new
            {
                x.TenantId,
                x.BackupFrequency,
                x.BackupTime,
                x.BackupTimeZoneId,
                x.LastBackupAtUtc,
            })
            .ToListAsync(ct);

        foreach (var item in scheduled)
        {
            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(item.BackupTimeZoneId!);
            }
            catch (TimeZoneNotFoundException)
            {
                continue;
            }
            catch (InvalidTimeZoneException)
            {
                continue;
            }
            if (
                !TimeOnly.TryParseExact(
                    item.BackupTime,
                    "HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var time
                )
            )
                continue;
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
            var due = IsDue(item.BackupFrequency!, time, localNow, item.LastBackupAtUtc, zone);
            if (!due)
                continue;

            try
            {
                using var tenantScope = scopes.CreateScope();
                await tenantScope
                    .ServiceProvider.GetRequiredService<StoragePolicyDispatcher>()
                    .BackupTenantAsync(item.TenantId, ct);
                var freshDb = tenantScope.ServiceProvider.GetRequiredService<CloudStorageDbContext>();
                var policy = await freshDb
                    .StoragePolicies.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(x => x.TenantId == item.TenantId, ct);
                if (policy is not null && policy.Mode == "backup")
                {
                    policy.MarkBackupCompleted(nowUtc);
                    await freshDb.SaveChangesAsync(ct);
                }
                logger.LogInformation("Cloud provider backup completed for tenant {TenantId}.", item.TenantId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Cloud provider backup failed for tenant {TenantId}; it will retry.",
                    item.TenantId
                );
            }
        }
    }

    private static bool IsDue(
        string frequency,
        TimeOnly scheduledTime,
        DateTime localNow,
        DateTime? lastBackupUtc,
        TimeZoneInfo zone
    )
    {
        if (TimeOnly.FromDateTime(localNow) < scheduledTime)
            return false;
        if (lastBackupUtc is null)
            return true;
        var lastLocal = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(lastBackupUtc.Value, DateTimeKind.Utc),
            zone
        );
        return frequency switch
        {
            "daily" => lastLocal.Date < localNow.Date,
            "weekly" => StartOfWeek(lastLocal.Date) < StartOfWeek(localNow.Date),
            "monthly" => lastLocal.Year < localNow.Year
                || lastLocal.Year == localNow.Year && lastLocal.Month < localNow.Month,
            _ => false,
        };
    }

    private static DateTime StartOfWeek(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
