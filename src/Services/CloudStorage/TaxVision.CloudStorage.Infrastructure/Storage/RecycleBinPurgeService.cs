using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.RecycleBin;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>
/// Fase C1 — job diario que borra definitivamente lo que lleva mas de
/// RecycleBinRetentionDays en la papelera: objeto de MinIO, fila de FileObject y
/// libera la cuota según el estado previo (QuotaRelease). Mismo patron que
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

    private const int BatchSize = 100;

    private async Task PurgeAsync(CancellationToken ct)
    {
        try
        {
            var purgedFiles = 0;
            foreach (var (tenantId, fileId) in await ListExpiredFilesAsync(ct))
            {
                if (await PurgeFileAsync(tenantId, fileId, ct))
                    purgedFiles++;
            }

            var purgedTrees = await PurgeFolderTreesAsync(ct);
            if (purgedFiles > 0 || purgedTrees > 0)
                logger.LogInformation(
                    "Purged {Files} files and {Folders} folder trees past recycle-bin retention.",
                    purgedFiles,
                    purgedTrees
                );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "CloudStorage recycle-bin purge failed.");
        }
    }

    private async Task<IReadOnlyList<(Guid TenantId, Guid FileId)>> ListExpiredFilesAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var files = scope.ServiceProvider.GetRequiredService<IFileObjectRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();
        var expired = await files.ListPurgeablePastRetentionAsync(clock.UtcNow, BatchSize, ct);
        return expired.Select(file => (file.TenantId, file.Id)).ToList();
    }

    // Una unidad por archivo: con el RowVersion de Files (CST-6), un conflicto solo deja ese archivo para mañana.
    private async Task<bool> PurgeFileAsync(Guid tenantId, Guid fileId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var files = services.GetRequiredService<IFileObjectRepository>();
        var clock = services.GetRequiredService<ISystemClock>();

        // RBAC Fase 5 — limits.GetAsync(file.TenantId) pasa por el HasQueryFilter fail-closed.
        services.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var file = await files.GetAsync(tenantId, fileId, ct);
        if (file is not { Status: FileStatus.SoftDeleted } || file.SoftDeleteExpiresAtUtc > clock.UtcNow)
            return false;

        try
        {
            var purged = await RecycleBinPurger.PurgeAsync(
                file,
                "retention-expired",
                services.GetRequiredService<IOptions<CloudStorageOptions>>().Value,
                files,
                services.GetRequiredService<IStorageLimitRepository>(),
                services.GetRequiredService<IStorageAuditRepository>(),
                services.GetRequiredService<IObjectStorage>(),
                services.GetRequiredService<IMultipartUploadStorage>(),
                clock,
                actorId: null,
                correlationId: $"recyclebin-purge-{file.Id:N}",
                logger,
                ct
            );
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
            return purged;
        }
        catch (ConflictException)
        {
            logger.LogWarning("Recycle-bin purge of file {FileId} lost a concurrent change; retried next run.", fileId);
            return false;
        }
    }

    // Carpetas borradas cuya retención venció: sus archivos ya los limpió el paso anterior (comparten
    // SoftDeleteExpiresAtUtc); acá solo se eliminan las filas de carpeta.
    private async Task<int> PurgeFolderTreesAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var folders = scope.ServiceProvider.GetRequiredService<IFolderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();
        var roots = await folders.ListPurgeableRootsPastRetentionAsync(clock.UtcNow, BatchSize, ct);
        foreach (var root in roots)
        {
            foreach (var folder in await folders.ListBatchAsync(root.TenantId, root.Id, ct))
                folders.Remove(folder);
        }

        if (roots.Count > 0)
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        return roots.Count;
    }
}
