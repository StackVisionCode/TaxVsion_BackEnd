using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Commands;

/// <summary>
/// Validacion + reserva de cuota + registro del FileObject compartidos entre
/// InitiateUploadHandler (presigned POST de un solo tiro) e
/// InitiateMultipartUploadHandler (Fase U) — ambos arrancan un upload nuevo con
/// exactamente las mismas reglas (scope, whitelist por FolderType/plan, cuota,
/// ObjectKey). Lo unico que difiere entre los dos es COMO se genera la URL de
/// subida despues de esto, asi que eso queda afuera del helper.
/// </summary>
internal static class UploadRegistration
{
    public static async Task<Result<FileObject>> ReserveAndRegisterAsync(
        Guid tenantId,
        Guid actorId,
        StorageActorScope scope,
        OwnerType ownerType,
        Guid? ownerId,
        FolderType folderType,
        int? taxYear,
        string originalName,
        string contentType,
        long sizeBytes,
        string correlationId,
        IStorageLimitRepository limits,
        IObjectKeyBuilder keyBuilder,
        CloudStorageOptions config,
        ISystemClock clock,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!scope.CanCreate(ownerType, ownerId))
            return Result.Failure<FileObject>(FileErrors.Forbidden);

        var extension = Path.GetExtension(originalName).ToLowerInvariant();

        var limit = await limits.GetAsync(tenantId, ct);
        if (limit is null)
            return Result.Failure<FileObject>(QuotaErrors.NotProvisioned);

        var policy = config.ResolveUploadPolicy(limit.PlanCode, folderType);
        if (string.IsNullOrWhiteSpace(originalName) || originalName.Length > 255)
            return Result.Failure<FileObject>(FileErrors.UnsupportedType);

        // Chequeada aparte de extension/content-type: antes las 3 condiciones caian en
        // el mismo File.UnsupportedType generico, y un archivo rechazado solo por
        // tamano (ej. una grabacion de meeting sobre el MaxFileSizeBytes de un plan
        // "starter") mostraba "tipo de archivo no permitido" — enganoso, llevaba a
        // debuggear la whitelist de tipos en vez del limite real de tamano del plan.
        if (sizeBytes > policy.MaxFileSizeBytes)
            return Result.Failure<FileObject>(FileErrors.FileTooLarge);

        if (
            !policy.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || !policy.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase)
        )
            return Result.Failure<FileObject>(FileErrors.UnsupportedType);

        var reserve = limit.Reserve(sizeBytes);
        if (reserve.IsFailure)
        {
            if (reserve.Error == QuotaErrors.Exceeded)
            {
                await bus.PublishAsync(
                    new StorageLimitExceededIntegrationEvent
                    {
                        TenantId = tenantId,
                        AttemptedFileSizeBytes = sizeBytes,
                        UsedBytes = limit.UsedBytes,
                        ReservedBytes = limit.ReservedBytes,
                        MaxBytes = limit.MaxBytes,
                        CorrelationId = correlationId,
                    }
                );
            }
            return Result.Failure<FileObject>(reserve.Error);
        }

        var fileId = Guid.NewGuid();
        var key = keyBuilder.Build(fileId, tenantId, ownerType, ownerId, folderType, taxYear, originalName);
        if (key.IsFailure)
        {
            limit.Release(sizeBytes);
            return Result.Failure<FileObject>(key.Error);
        }

        var registered = FileObject.Register(
            fileId,
            tenantId,
            ownerType,
            ownerId,
            folderType,
            taxYear,
            key.Value,
            Path.GetFileName(originalName),
            contentType,
            sizeBytes,
            actorId,
            clock.UtcNow,
            clock.UtcNow.AddHours(Math.Clamp(config.UploadReservationHours, 1, 168))
        );
        if (registered.IsFailure)
            limit.Release(sizeBytes);

        return registered;
    }
}
