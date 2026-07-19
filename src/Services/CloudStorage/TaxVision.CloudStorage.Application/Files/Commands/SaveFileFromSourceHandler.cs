using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
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
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SaveFileRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
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

            var policy = options.Value.ResolveUploadPolicy(limit.PlanCode, folderType);
            var extension = Path.GetExtension(evt.OriginalName).ToLowerInvariant();
            if (
                !policy.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                || !policy.AllowedContentTypes.Contains(evt.ContentType, StringComparer.OrdinalIgnoreCase)
                || evt.SizeBytes > policy.MaxFileSizeBytes
            )
            {
                logger.LogWarning(
                    "SaveFileRequested {FileId} from {Service} rejected by upload policy (extension/content-type/size).",
                    evt.FileId,
                    evt.RequestingService
                );
                return;
            }

            var reserve = limit.Reserve(evt.SizeBytes);
            if (reserve.IsFailure)
            {
                logger.LogWarning(
                    "SaveFileRequested {FileId} from {Service} rejected: {Error}.",
                    evt.FileId,
                    evt.RequestingService,
                    reserve.Error.Code
                );
                return;
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
                limit.Release(evt.SizeBytes);
                logger.LogError(
                    "SaveFileRequested {FileId} from {Service} failed to build an object key: {Error}.",
                    evt.FileId,
                    evt.RequestingService,
                    key.Error.Code
                );
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
                evt.SizeBytes,
                evt.ActorId,
                clock.UtcNow,
                clock.UtcNow // ya esta subido — nunca queda observable en PendingUpload, ver MarkPendingScan abajo
            );
            if (registered.IsFailure)
            {
                limit.Release(evt.SizeBytes);
                logger.LogError(
                    "SaveFileRequested {FileId} from {Service} failed to register: {Error}.",
                    evt.FileId,
                    evt.RequestingService,
                    registered.Error.Code
                );
                return;
            }

            var file = registered.Value;

            // Idempotencia ante redelivery: Copy/Delete en MinIO no son transaccionales con el
            // SaveChanges de abajo. Si un intento anterior copio el objeto pero el SaveChanges
            // fallo despues (p.ej. DbUpdateConcurrencyException en TenantStorageLimits por otro
            // mensaje concurrente del mismo tenant), el retry de Wolverine reenvia el mismo
            // evento — y sin este chequeo, intentaria copiar de nuevo desde SourceObjectKey, que
            // el intento anterior ya borro, tirando ObjectNotFoundException para siempre.
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

            file.MarkPendingScan();
            files.Add(file);
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
            await bus.PublishAsync(new ScanFileCommand(evt.TenantId, file.Id, correlation.CorrelationId));
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
