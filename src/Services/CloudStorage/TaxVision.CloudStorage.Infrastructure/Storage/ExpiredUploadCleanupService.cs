using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

public sealed class ExpiredUploadCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExpiredUploadCleanupService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await CleanupAsync(stoppingToken);
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var files = scope.ServiceProvider.GetRequiredService<IFileObjectRepository>();
            var limits = scope.ServiceProvider.GetRequiredService<IStorageLimitRepository>();
            var audit = scope.ServiceProvider.GetRequiredService<IStorageAuditRepository>();
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
            var multipartStorage = scope.ServiceProvider.GetRequiredService<IMultipartUploadStorage>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<CloudStorageOptions>>().Value;
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            var expired = await files.ListExpiredUploadsAsync(clock.UtcNow, 100, ct);
            foreach (var file in expired)
            {
                if (file.ExpireUpload(clock.UtcNow).IsFailure)
                    continue;

                // RBAC Fase 5 — limits.GetAsync ahora pasa por el HasQueryFilter fail-closed; sin
                // sellar el tenant efectivo por-item, la cuota real quedaría invisible para el lookup.
                tenantContext.SetTenant(file.TenantId);
                (await limits.GetAsync(file.TenantId, ct))?.Release(file.SizeBytes);
                // Fase U — un upload multiparte nunca ensamblado no existe como objeto
                // todavia: DeleteAsync sobre esa key es un no-op silencioso y las partes
                // ya subidas quedan huerfanas en MinIO para siempre. Hay que abortar la
                // sesion multiparte especificamente para liberarlas.
                if (file.MultipartUploadId is { } uploadId)
                    await multipartStorage.AbortAsync(options.TempBucket, file.ObjectKey, uploadId, ct);
                else
                    await storage.DeleteAsync(options.TempBucket, file.ObjectKey, ct);
                audit.Add(
                    StorageAccessLog.Create(
                        file.TenantId,
                        file.Id,
                        file.CreatedBy,
                        "upload.expired",
                        "released",
                        null,
                        null,
                        $"cleanup-{file.Id:N}",
                        null,
                        clock.UtcNow
                    )
                );
            }

            if (expired.Count > 0)
            {
                await unitOfWork.SaveChangesAsync(ct);
                logger.LogInformation("Released {Count} expired CloudStorage upload reservations.", expired.Count);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Expired CloudStorage upload cleanup failed.");
        }
    }
}
