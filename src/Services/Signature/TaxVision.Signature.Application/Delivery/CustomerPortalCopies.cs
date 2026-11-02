using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Delivery;

/// <summary>
/// Cuando firman VARIOS clientes, el documento firmado no puede tener un solo dueño en el portal: cada
/// cliente recibe una copia en su carpeta "Signed Documents" (copia en servidor de CloudStorage, sin mover
/// bytes). Se copian solo los archivos ya disponibles; los que faltan los toma el siguiente disparador.
/// </summary>
public sealed class CustomerPortalCopier(
    ISignatureCloudStorageClient storage,
    IFileMetadataRefRepository files,
    IDistributedLock distributedLock,
    ILogger<CustomerPortalCopier> logger
)
{
    private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(2);

    /// <summary>Copias hechas en esta pasada (0 si no había nada pendiente o disponible).</summary>
    public async Task<int> CopyAvailableAsync(SignatureRequest request, Guid? availableFileId, CancellationToken ct)
    {
        if (request.PendingCustomerCopies().Count == 0)
            return 0;

        // Un archivo se copia una vez por cliente: dos disparadores a la vez se serializan por solicitud.
        await using var handle = await distributedLock.AcquireAsync($"signature:portal-copies:{request.Id:N}", LockTtl, ct);
        if (!handle.IsAcquired)
            throw new InvalidOperationException($"Portal copies for request {request.Id} are in progress elsewhere; retrying.");

        var copied = 0;
        var taxYear = (request.CompletedAtUtc ?? request.CreatedAtUtc).Year;
        foreach (var pending in request.PendingCustomerCopies())
        {
            if (pending.SourceFileId != availableFileId && !await IsAvailableAsync(request.TenantId, pending.SourceFileId, ct))
                continue;

            var copy = await storage.CopyToCustomerAsync(request.TenantId, pending.SourceFileId, pending.CustomerId, taxYear, ct);
            if (copy.IsFailure)
            {
                logger.LogWarning(
                    "Portal copy of {FileId} for customer {CustomerId} (request {RequestId}) failed: {Error}",
                    pending.SourceFileId,
                    pending.CustomerId,
                    request.Id,
                    copy.Error.Message
                );
                continue;
            }

            request.RecordCustomerCopy(pending.SourceFileId, pending.CustomerId, copy.Value, DateTime.UtcNow);
            copied++;
        }
        return copied;
    }

    // Archivos anteriores a la proyección local (solicitudes viejas) se consultan en CloudStorage.
    private async Task<bool> IsAvailableAsync(Guid tenantId, Guid fileId, CancellationToken ct)
    {
        var projection = await files.GetByFileIdAsync(tenantId, fileId, ct);
        if (projection is not null)
            return projection.Status == FileScanStatus.Available;
        var remote = await storage.GetFileAsync(tenantId, fileId, ct);
        return remote.IsSuccess && string.Equals(remote.Value.Status, "Available", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Disparador principal: un archivo final de la solicitud quedó disponible.</summary>
public static class CustomerPortalCopiesConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent evt,
        ISignatureRequestRepository repository,
        CustomerPortalCopier copier,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId))
        {
            var request =
                await repository.GetBySealedFileIdAsync(evt.TenantId, evt.FileId, ct)
                ?? await repository.GetByCertificateFileIdAsync(evt.TenantId, evt.FileId, ct);
            if (request is null)
                return;

            if (await copier.CopyAvailableAsync(request, evt.FileId, ct) > 0)
                await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

/// <summary>Resultado del backfill: solicitudes revisadas, copias hechas y las que siguen pendientes.</summary>
public sealed record PortalCopiesBackfillReport(
    bool DryRun,
    int RequestsScanned,
    int RequestsWithPendingCopies,
    int CopiesCreated,
    int CopiesStillPending
);

/// <summary>
/// Una sola vez: las solicitudes de varios clientes completadas ANTES de F2b no tienen copias en el portal de
/// cada cliente. Las genera con el mismo copiador (idempotente). <see cref="DryRun"/> = solo cuenta.
/// </summary>
public sealed record BackfillCustomerPortalCopiesCommand(Guid TenantId, bool DryRun);

public static class BackfillCustomerPortalCopiesHandler
{
    public static async Task<BuildingBlocks.Results.Result<PortalCopiesBackfillReport>> Handle(
        BackfillCustomerPortalCopiesCommand command,
        ISignatureRequestRepository repository,
        CustomerPortalCopier copier,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var requests = await repository.ListCompletedWithSeveralCustomerSignersAsync(command.TenantId, ct);
        var withPending = 0;
        var created = 0;
        var stillPending = 0;

        foreach (var request in requests)
        {
            var pending = request.PendingCustomerCopies().Count;
            if (pending == 0)
                continue;
            withPending++;

            if (command.DryRun)
            {
                stillPending += pending;
                continue;
            }

            // Se guarda por solicitud: si algo falla a mitad, lo ya copiado queda registrado.
            var copied = await copier.CopyAvailableAsync(request, availableFileId: null, ct);
            if (copied > 0)
                await unitOfWork.SaveChangesAsync(ct);
            created += copied;
            stillPending += request.PendingCustomerCopies().Count;
        }

        return BuildingBlocks.Results.Result.Success(
            new PortalCopiesBackfillReport(command.DryRun, requests.Count, withPending, created, stillPending)
        );
    }
}

/// <summary>
/// Disparador de respaldo, tras el commit del sellado: si el FileAvailable de un archivo llegó antes de que
/// la solicitud guardara su FileId (carrera con el commit), aquí se copia leyendo la proyección.
/// </summary>
public sealed record CreateCustomerPortalCopiesCommand(Guid TenantId, Guid SignatureRequestId);

public static class CreateCustomerPortalCopiesHandler
{
    public static async Task Handle(
        CreateCustomerPortalCopiesCommand command,
        ISignatureRequestRepository repository,
        CustomerPortalCopier copier,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(command.TenantId, command.SignatureRequestId, ct);
        if (request is null)
            return;

        if (await copier.CopyAvailableAsync(request, availableFileId: null, ct) > 0)
            await unitOfWork.SaveChangesAsync(ct);
    }
}
