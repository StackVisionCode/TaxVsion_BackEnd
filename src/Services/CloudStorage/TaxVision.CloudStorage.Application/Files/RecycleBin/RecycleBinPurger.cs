using Microsoft.Extensions.Logging;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Files.RecycleBin;

/// <summary>
/// Fase C1 — borrado FISICO de un archivo que ya esta en la papelera: mismo
/// procedimiento sin importar quien lo dispare (EmptyRecycleBinHandler, manual, o
/// RecycleBinPurgeService, el job diario por vencimiento) — un solo lugar evita
/// que las dos rutas terminen reimplementando el mismo borrado con detalles
/// distintos (la leccion de RunCustomerImportHandler).
/// </summary>
public static class RecycleBinPurger
{
    /// <summary>
    /// Fase L1.2 — devuelve false (sin purgar) si el archivo esta bajo legal hold.
    /// Un hold puede colocarse DESPUES de que el archivo ya entro a la papelera
    /// (SoftDelete solo bloquea el hold al momento del borrado); sin este guard,
    /// el job diario lo purgaria igual al vencer la retencion.
    /// </summary>
    public static async Task<bool> PurgeAsync(
        FileObject file,
        string trigger,
        string mainBucket,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectStorage storage,
        ISystemClock clock,
        Guid? actorId,
        string correlationId,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (file.IsLegalHeld)
        {
            audit.Add(
                StorageAccessLog.Create(
                    file.TenantId,
                    file.Id,
                    actorId ?? file.CreatedBy,
                    "delete.purge-skipped",
                    "blocked",
                    null,
                    null,
                    correlationId,
                    $"trigger={trigger};reason=legal-hold",
                    clock.UtcNow
                )
            );
            return false;
        }

        await storage.DeleteAsync(mainBucket, file.ObjectKey, ct);

        var limit = await limits.GetAsync(file.TenantId, ct);
        if (limit is null)
        {
            // Mismo tipo de gap que SaveFileFromSourceHandler (bug real de produccion,
            // Tenant_Service_LogoSupport_Plan.md): sin esta rama, un tenant sin fila de
            // cuota (nunca provisionada, o borrada por error) purgaba archivos fisicos sin
            // dejar ningun rastro de que su UsedBytes quedo desactualizado — silencioso para
            // siempre. Este job es un BackgroundService, no un consumer de Wolverine, asi que
            // no hay retry/dead-letter posible; lo maximo que se puede hacer es loguearlo alto
            // y seguir (el borrado fisico ya se hizo, no tiene sentido revertirlo).
            logger.LogError(
                "RecycleBinPurger: tenant {TenantId} has no quota row — file {FileId} ({SizeBytes} bytes) "
                    + "was purged from storage but UsedBytes was NOT released. Quota accounting for this "
                    + "tenant is now stale.",
                file.TenantId,
                file.Id,
                file.SizeBytes
            );
        }
        else
        {
            limit.ReleaseUsed(file.SizeBytes);
        }

        audit.Add(
            StorageAccessLog.Create(
                file.TenantId,
                file.Id,
                actorId ?? file.CreatedBy,
                "delete.purge",
                "success",
                null,
                null,
                correlationId,
                $"trigger={trigger};size={file.SizeBytes}",
                clock.UtcNow
            )
        );

        files.Remove(file);
        return true;
    }
}
