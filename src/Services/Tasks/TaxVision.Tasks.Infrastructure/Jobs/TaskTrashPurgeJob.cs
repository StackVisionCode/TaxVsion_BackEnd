using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Infrastructure.Jobs;

/// <summary>
/// Borra de verdad lo que lleva más de 30 días en la papelera (D-25), una rama por transacción: si una falla, las demás
/// siguen. Con la tarea se van su tiempo, sus archivos referenciados y su historial.
/// </summary>
public sealed class TaskTrashPurgeJob(IServiceProvider serviceProvider, ILogger<TaskTrashPurgeJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Task trash purge failed; retrying next interval.");
            }
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<(Guid TenantId, Guid RootId)> expired;
        using (var scope = serviceProvider.CreateScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<ITaskTrashReader>();
            expired = await reader.ListExpiredRootsAsync(
                DateTime.UtcNow.AddDays(-TaskItem.TrashRetentionDays),
                BatchSize,
                ct
            );
        }

        var purged = 0;
        foreach (var (tenantId, rootId) in expired)
            purged += await PurgeAsync(tenantId, rootId, ct) ? 1 : 0;
        if (expired.Count > 0)
            logger.LogInformation("Task trash purge removed {Purged} of {Expired} branches.", purged, expired.Count);
    }

    private async Task<bool> PurgeAsync(Guid tenantId, Guid rootId, CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var tasks = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
            foreach (var task in await tasks.ListTrashBranchAsync(tenantId, rootId, ct))
                tasks.Remove(task);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not purge trashed task {TaskId}.", rootId);
            return false;
        }
    }
}
