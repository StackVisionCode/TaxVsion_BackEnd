using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.RecycleBin;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>
/// Fase C1 — job diario que borra definitivamente lo que lleva mas de
/// RecycleBinRetentionDays en la papelera: objeto de MinIO, fila de FileObject y
/// libera la cuota (TenantStorageLimit.ReleaseUsed). Mismo patron que
/// ExpiredUploadCleanupService (PeriodicTimer + scope por tick + try/catch que
/// nunca tumba el host).
/// </summary>
public sealed class RecycleBinPurgeService(IServiceScopeFactory scopeFactory, ILogger<RecycleBinPurgeService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PurgeAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PurgeAsync(stoppingToken);
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var files = scope.ServiceProvider.GetRequiredService<IFileObjectRepository>();
            var limits = scope.ServiceProvider.GetRequiredService<IStorageLimitRepository>();
            var audit = scope.ServiceProvider.GetRequiredService<IStorageAuditRepository>();
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<CloudStorageOptions>>().Value;
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            var expired = await files.ListPurgeablePastRetentionAsync(clock.UtcNow, 100, ct);
            foreach (var file in expired)
            {
                // RBAC Fase 5 — RecycleBinPurger.PurgeAsync llama a limits.GetAsync(file.TenantId),
                // que ahora pasa por el HasQueryFilter fail-closed; sin sellar el tenant efectivo
                // por-item, la cuota real del tenant quedaría invisible para ese lookup.
                tenantContext.SetTenant(file.TenantId);
                await RecycleBinPurger.PurgeAsync(
                    file,
                    "retention-expired",
                    options.MainBucket,
                    files,
                    limits,
                    audit,
                    storage,
                    clock,
                    actorId: null,
                    correlationId: $"recyclebin-purge-{file.Id:N}",
                    logger,
                    ct
                );
            }

            if (expired.Count > 0)
            {
                await unitOfWork.SaveChangesAsync(ct);
                logger.LogInformation("Purged {Count} CloudStorage files past recycle-bin retention.", expired.Count);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "CloudStorage recycle-bin purge failed.");
        }
    }
}
