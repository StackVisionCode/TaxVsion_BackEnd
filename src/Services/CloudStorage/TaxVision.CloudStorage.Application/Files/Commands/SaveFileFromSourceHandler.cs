using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Commands;

/// <summary>
/// Fase D0 — consumer de <see cref="SaveFileRequestedIntegrationEvent"/>: reemplaza
/// el patron HTTP+M2M initiate/PUT/complete para servicios de negocio que ya subieron
/// el objeto directo a MinIO (IAM propio, scoped a su prefijo en TempBucket). Registra
/// el FileObject con el FileId que trae el evento (no uno nuevo — ver el docblock del
/// evento), lo mueve a la key canonica de CloudStorage dentro de TempBucket, y dispara
/// el mismo ScanFileCommand que usa el flujo de upload normal: desde aca en adelante
/// es indistinguible de un archivo subido por un usuario.
/// </summary>
public static class SaveFileFromSourceHandler
{
    public static async Task Handle(
        SaveFileRequestedIntegrationEvent evt,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectKeyBuilder keyBuilder,
        IObjectStorage storage,
        Folders.ISystemFolderProvisioner systemFolders,
        IOptions<CloudStorageOptions> options,
        Quotas.IUploadPolicyResolver uploadPolicies,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SaveFileRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        Task Reject(string reason) =>
            RejectAsync(evt, reason, audit, storage, unitOfWork, bus, correlation, clock, logger, ct);

        using (correlation.Push(evt.CorrelationId))
        {
            // Redelivery del mismo evento (at-least-once) tras un crash post-persist:
            // el FileId ya existe, no hay nada mas que hacer.
            if (await files.GetAsync(evt.TenantId, evt.FileId, ct) is not null)
            {
                logger.LogInformation(
                    "SaveFileRequested {FileId} already registered — redelivery, skipping.",
                    evt.FileId
                );
                return;
            }

            if (
                !Enum.TryParse<OwnerType>(evt.OwnerType, ignoreCase: true, out var ownerType)
                || !Enum.TryParse<FolderType>(evt.FolderType, ignoreCase: true, out var folderType)
            )
            {
                logger.LogError(
                    "SaveFileRequested {FileId} from {Service} has an invalid OwnerType/FolderType ({OwnerType}/{FolderType}) — dropping.",
                    evt.FileId,
                    evt.RequestingService,
                    evt.OwnerType,
                    evt.FolderType
                );
                await Reject("InvalidOwnerOrFolder");
                return;
            }

            var limit = await limits.GetAsync(evt.TenantId, ct);
            if (limit is null)
            {
                // A recien-creado tenant's TenantStorageLimits llega via un fanout de 2 saltos
                // (Tenant -> Subscription TenantCreatedConsumer -> TenantEntitlementsChangedIntegrationEvent
                // -> CloudStorage TenantEntitlementsChangedQuotaConsumer), asincrono respecto de este
                // consumer — a diferencia del OwnerType/FolderType invalido de arriba (nunca se corrige
                // reintentando), esta condicion SI es transitoria: throw en vez de log+return para que
                // el RetryWithCooldown(1s/5s/15s) de Program.cs reintente, y si el tenant sigue sin
                // cuota tras 3 intentos, el mensaje caiga a la dead-letter queue (visible/reconciliable)
                // en vez de perderse en silencio para siempre — bug real de produccion encontrado en el
                // primer upload de un logo de tenant nuevo (Tenant_Service_LogoSupport_Plan.md).
                throw new InvalidOperationException(
                    $"SaveFileRequested {evt.FileId}: tenant {evt.TenantId} has no quota provisioned yet."
                );
            }

            var key = keyBuilder.Build(
                evt.FileId,
                evt.TenantId,
                ownerType,
                evt.OwnerId,
                folderType,
                evt.TaxYear,
                evt.OriginalName
            );
            if (key.IsFailure)
            {
                logger.LogError(
                    "SaveFileRequested {FileId} from {Service} failed to build an object key: {Error}.",
                    evt.FileId,
                    evt.RequestingService,
                    key.Error.Code
                );
                await Reject(key.Error.Code);
                return;
            }

            var sizeBytes = await MeasureAsync(evt, key.Value.Value, storage, options.Value.TempBucket, logger, ct);

            // 0 bytes no es un archivo: antes caia en Reserve() como "FileTooLarge" y confundia al diagnosticar.
            if (sizeBytes <= 0)
            {
                logger.LogWarning(
                    "SaveFileRequested {FileId} from {Service} rejected: empty file.",
                    evt.FileId,
                    evt.RequestingService
                );
                await Reject("EmptyFile");
                return;
            }

            var policy = await uploadPolicies.ResolveAsync(
                evt.TenantId,
                limit,
                folderType,
                forCustomerPortal: false,
                ct
            );
            var extension = Path.GetExtension(evt.OriginalName).ToLowerInvariant();
            if (
                !policy.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                || !policy.AllowedContentTypes.Contains(evt.ContentType, StringComparer.OrdinalIgnoreCase)
                || sizeBytes > policy.MaxFileSizeBytes
            )
            {
                logger.LogWarning(
                    "SaveFileRequested {FileId} from {Service} rejected by upload policy (extension/content-type/size).",
                    evt.FileId,
                    evt.RequestingService
                );
                await Reject("UploadPolicy");
                return;
            }

            var reserve = limit.Reserve(sizeBytes);
            if (reserve.IsFailure)
            {
                logger.LogWarning(
                    "SaveFileRequested {FileId} from {Service} rejected: {Error}.",
                    evt.FileId,
                    evt.RequestingService,
                    reserve.Error.Code
                );
                await Reject(reserve.Error.Code);
                return;
            }

            var registered = FileObject.Register(
                evt.FileId,
                evt.TenantId,
                ownerType,
                evt.OwnerId,
                folderType,
                evt.TaxYear,
                key.Value,
                evt.OriginalName,
                evt.ContentType,
                sizeBytes,
                evt.ActorId,
                clock.UtcNow,
                clock.UtcNow // ya esta subido — nunca queda observable en PendingUpload, ver MarkPendingScan abajo
            );
            if (registered.IsFailure)
            {
                limit.Release(sizeBytes);
                logger.LogError(
                    "SaveFileRequested {FileId} from {Service} failed to register: {Error}.",
                    evt.FileId,
                    evt.RequestingService,
                    registered.Error.Code
                );
                await Reject(registered.Error.Code);
                return;
            }

            var file = registered.Value;
            file.MarkPendingScan(clock.UtcNow);

            // Coloca el archivo en su carpeta de sistema segun FolderType (get-or-create por
            // category, idempotente). Tipos internos devuelven null y se quedan en raiz. Cierra
            // el hueco historico: los guardados M2M nunca aterrizaban en una carpeta navegable.
            if (options.Value.AutoSystemFolders)
            {
                var systemFolderId = await systemFolders.ResolveFolderIdAsync(
                    evt.TenantId,
                    ownerType,
                    evt.OwnerId,
                    folderType,
                    evt.ActorId,
                    clock.UtcNow,
                    ct
                );
                if (systemFolderId is { } folderId)
                    file.MoveToFolder(folderId, clock.UtcNow);
            }

            files.Add(file);

            // La reserva se decide antes de mover el objeto: si otra se llevó el cupo, no se copia nada.
            try
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (ConflictException conflict) when (conflict.Code == QuotaErrors.ConcurrentExceededCode)
            {
                files.Remove(file);
                limit.Release(sizeBytes);
                logger.LogWarning(
                    "SaveFileRequested {FileId} from {Service} rejected: a concurrent reservation took the quota.",
                    evt.FileId,
                    evt.RequestingService
                );
                await Reject("QuotaExceeded");
                return;
            }

            // Idempotencia ante redelivery: Copy/Delete en MinIO no son transaccionales con el
            // commit. Si un intento anterior copio el objeto pero la transaccion fallo despues,
            // el retry de Wolverine reenvia el mismo evento — y sin este chequeo, intentaria
            // copiar de nuevo desde SourceObjectKey, que el intento anterior ya borro, tirando
            // ObjectNotFoundException para siempre.
            if (!await storage.ExistsAsync(options.Value.TempBucket, key.Value.Value, ct))
            {
                await storage.CopyAsync(
                    evt.SourceBucket,
                    evt.SourceObjectKey,
                    options.Value.TempBucket,
                    key.Value.Value,
                    ct
                );
                await storage.DeleteAsync(evt.SourceBucket, evt.SourceObjectKey, ct);
            }

            audit.Add(
                StorageAccessLog.Create(
                    evt.TenantId,
                    file.Id,
                    evt.ActorId,
                    "upload.from-source",
                    "success",
                    null,
                    null,
                    correlation.CorrelationId,
                    $"service={evt.RequestingService}",
                    clock.UtcNow
                )
            );
            await unitOfWork.SaveChangesAsync(ct);
            await bus.PublishAsync(new ScanFileCommand(evt.TenantId, file.Id, correlation.CorrelationId));
        }
    }

    /// <summary>Tamaño real del objeto. En un reintento el origen ya pudo pasar a la key canónica.</summary>
    private static async Task<long> MeasureAsync(
        SaveFileRequestedIntegrationEvent evt,
        string canonicalKey,
        IObjectStorage storage,
        string tempBucket,
        ILogger logger,
        CancellationToken ct
    )
    {
        var actualSize = await storage.ExistsAsync(tempBucket, canonicalKey, ct)
            ? await storage.GetSizeAsync(tempBucket, canonicalKey, ct)
            : await storage.GetSizeAsync(evt.SourceBucket, evt.SourceObjectKey, ct);

        if (actualSize != evt.SizeBytes)
            logger.LogWarning(
                "SaveFileRequested {FileId} from {Service} declared {DeclaredBytes} bytes but the object has {ActualBytes}.",
                evt.FileId,
                evt.RequestingService,
                evt.SizeBytes,
                actualSize
            );
        return actualSize;
    }

    /// <summary>
    /// Rechazo definitivo: queda auditado y el servicio que lo pidió se entera
    /// (<see cref="FileSaveRejectedIntegrationEvent"/>), porque ya pudo apuntar ese FileId en su aggregate.
    /// </summary>
    private static async Task RejectAsync(
        SaveFileRequestedIntegrationEvent evt,
        string reason,
        IStorageAuditRepository audit,
        IObjectStorage storage,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISystemClock clock,
        ILogger logger,
        CancellationToken ct
    )
    {
        audit.Add(
            StorageAccessLog.Create(
                evt.TenantId,
                evt.FileId,
                evt.ActorId,
                "upload.from-source",
                "rejected",
                null,
                null,
                correlation.CorrelationId,
                $"service={evt.RequestingService};reason={reason}",
                clock.UtcNow
            )
        );
        await bus.PublishAsync(
            new FileSaveRejectedIntegrationEvent
            {
                TenantId = evt.TenantId,
                FileId = evt.FileId,
                RequestingService = evt.RequestingService,
                OwnerType = evt.OwnerType,
                OwnerId = evt.OwnerId,
                FolderType = evt.FolderType,
                Reason = reason,
                CorrelationId = correlation.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);

        // Va al final: si no se pudo guardar, el reintento necesita el origen. Lo que quede lo borra la regla de 24 h.
        try
        {
            await storage.DeleteAsync(evt.SourceBucket, evt.SourceObjectKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SaveFileRequested {FileId}: rejected source could not be deleted.", evt.FileId);
        }
    }
}
