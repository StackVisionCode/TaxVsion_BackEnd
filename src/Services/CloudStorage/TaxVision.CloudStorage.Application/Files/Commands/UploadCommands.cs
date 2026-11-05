using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.CustomerAccess;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Application.Platform;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.CustomerAccess;
using TaxVision.CloudStorage.Domain.Files;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Commands;

/// <summary>
/// Gate de Upload deny per-folder compartido entre single-shot (InitiateUploadHandler) y multipart
/// (InitiateMultipartUploadHandler): resuelve la carpeta de sistema destino via el catalogo (misma
/// identidad por categoria que usa el provisioner tras completar la subida) y devuelve el error
/// cloudstorage.customer_access.denied con el reason si el portal tiene Upload deny ahi. Fail-closed:
/// staff y portals sin reglas quedan afuera (HasAnyPortalDenyRules es false si el flag per-item esta
/// off o no hay reglas), por lo que no se golpea la BD de Folders en el camino caliente.
/// </summary>
internal static class PortalUploadDenyGuard
{
    public static async Task<Error?> EnsurePortalUploadAllowedAsync(
        StorageActorScope scope,
        Guid tenantId,
        OwnerType ownerType,
        Guid? ownerId,
        FolderType folderType,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        if (!scope.IsCustomerPortal || !scope.HasAnyPortalDenyRules)
            return null;
        if (!SystemFolderCatalog.TryGet(folderType, out var spec))
            return null;
        var destination = await folders.GetByOwnerAndCategoryAsync(tenantId, ownerType, ownerId, spec.Category, ct);
        if (destination is null)
            return null;
        if (!scope.IsDeniedForPortal(AccessResourceType.Folder, destination.Id, AccessCapability.Upload))
            return null;
        return CustomerAccessDenied.With(
            scope.DenyReasonFor(AccessResourceType.Folder, destination.Id, AccessCapability.Upload)
        );
    }
}

public sealed record InitiateUploadCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    InitiateUploadRequest Request,
    RequestAuditContext Audit
);

public static class InitiateUploadHandler
{
    public static async Task<Result<InitiatedUploadResponse>> Handle(
        InitiateUploadCommand command,
        IFileObjectRepository files,
        IFolderRepository folders,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectKeyBuilder keyBuilder,
        ISystemFolderProvisioner systemFolders,
        IObjectStorage storage,
        IOptions<CloudStorageOptions> options,
        IStoragePlatformSettingsProvider platform,
        IUploadPolicyResolver uploadPolicies,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var request = command.Request;
        var config = options.Value;
        var platformSettings = await platform.GetAsync(ct);

        var denied = await PortalUploadDenyGuard.EnsurePortalUploadAllowedAsync(
            command.Scope,
            command.TenantId,
            request.OwnerType,
            request.OwnerId,
            request.FolderType,
            folders,
            ct
        );
        if (denied is not null)
            return Result.Failure<InitiatedUploadResponse>(denied);

        // El autor del archivo es quien subió; un usuario no puede subir a nombre de otro (se ignora).
        var uploaderId =
            command.Scope.IsService && request.OnBehalfOfUserId is { } onBehalfOf && onBehalfOf != Guid.Empty
                ? onBehalfOf
                : command.ActorId;
        var registered = await UploadRegistration.ReserveAndRegisterAsync(
            command.TenantId,
            uploaderId,
            command.Scope,
            request.OwnerType,
            request.OwnerId,
            request.FolderType,
            request.TaxYear,
            request.OriginalName,
            request.ContentType,
            request.SizeBytes,
            command.Audit.CorrelationId,
            limits,
            keyBuilder,
            systemFolders,
            platformSettings,
            uploadPolicies,
            clock,
            bus,
            ct
        );
        if (registered.IsFailure)
            return Result.Failure<InitiatedUploadResponse>(registered.Error);

        var file = registered.Value;
        files.Add(file);
        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                file.Id,
                command.ActorId,
                "upload.initiated",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                uploaderId == command.ActorId
                    ? $"size={request.SizeBytes}"
                    : $"size={request.SizeBytes};on-behalf-of={uploaderId}",
                clock.UtcNow
            )
        );

        var lifetime = TimeSpan.FromMinutes(platformSettings.SignedUrlMinutes);
        var upload = await storage.CreateUploadPolicyAsync(
            config.TempBucket,
            file.ObjectKey,
            request.ContentType,
            request.SizeBytes,
            lifetime,
            ct
        );
        await bus.PublishAsync(
            new FileAccessAuditedIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = file.Id,
                ActorId = command.ActorId,
                Action = "upload.initiated",
                Outcome = "success",
                IpAddress = command.Audit.IpAddress,
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new InitiatedUploadResponse(file.Id, upload.Url, upload.FormData, clock.UtcNow.Add(lifetime), file.Status)
        );
    }
}

public sealed record CompleteUploadCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid FileId,
    RequestAuditContext Audit
);

public static class CompleteUploadHandler
{
    public static async Task<Result> Handle(
        CompleteUploadCommand command,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectStorage storage,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
            return Result.Failure(FileErrors.NotFound);
        if (!command.Scope.CanAccess(file))
            return Result.Failure(FileErrors.NotFound);
        if (file.Status != FileStatus.PendingUpload)
            return Result.Failure(FileErrors.InvalidTransition);

        var actualSize = await storage.GetSizeAsync(options.Value.TempBucket, file.ObjectKey, ct);
        if (actualSize != file.SizeBytes)
        {
            var limit = await limits.GetAsync(command.TenantId, ct);
            limit?.Release(file.SizeBytes);
            file.RejectUpload(
                $"Declared size {file.SizeBytes} does not match uploaded size {actualSize}.",
                clock.UtcNow
            );
            await storage.DeleteAsync(options.Value.TempBucket, file.ObjectKey, ct);
            audit.Add(
                StorageAccessLog.Create(
                    command.TenantId,
                    file.Id,
                    command.ActorId,
                    "upload.completed",
                    "rejected",
                    command.Audit.IpAddress,
                    command.Audit.UserAgent,
                    command.Audit.CorrelationId,
                    $"declared={file.SizeBytes};actual={actualSize}",
                    clock.UtcNow
                )
            );
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure(FileErrors.UploadSizeMismatch);
        }

        var transition = file.MarkPendingScan(clock.UtcNow);
        if (transition.IsFailure)
            return transition;

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                file.Id,
                command.ActorId,
                "upload.completed",
                "pending-scan",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                null,
                clock.UtcNow
            )
        );
        await bus.PublishAsync(
            new ScanFileCommand(
                command.TenantId,
                file.Id,
                command.Audit.CorrelationId,
                command.Scope.IsCustomerPortal ? command.Scope.CustomerId : null
            )
        );
        await bus.PublishAsync(
            new FileAccessAuditedIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = file.Id,
                ActorId = command.ActorId,
                Action = "upload.completed",
                Outcome = "pending-scan",
                IpAddress = command.Audit.IpAddress,
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ScanFileCommand(Guid TenantId, Guid FileId, string CorrelationId, Guid? PortalCustomerId = null);

public static class ScanFileHandler
{
    public static async Task Handle(
        ScanFileCommand command,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectStorage storage,
        IVirusScanner virusScanner,
        IContentScanner contentScanner,
        IFileContentInspector inspector,
        IOptions<CloudStorageOptions> options,
        IUploadPolicyResolver uploadPolicies,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<ScanFileCommand> logger,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
        {
            // Purgado mientras esperaba en cola: no va a aparecer, reintentar no sirve.
            logger.LogWarning("Scan skipped: file {FileId} no longer exists.", command.FileId);
            return;
        }

        // Terminales — un redelivery de ScanFileCommand no debe relanzar el scan. ScanFailed ya es
        // definitivo (su reserva se liberó); el reescaneo pasa por RescanFileCommand.
        if (
            file.Status
            is FileStatus.Available
                or FileStatus.Infected
                or FileStatus.BlockedByPolicy
                or FileStatus.PendingReview
                or FileStatus.ScanFailed
                or FileStatus.SoftDeleted
        )
            return;

        // Sin SaveChanges intermedio: dentro de la transacción no confirma y solo alarga el bloqueo (CST-3).
        var transition = file.MarkScanning();
        if (transition.IsFailure)
            throw new InvalidOperationException(transition.Error.Message);

        var sourceBucket = await LocateStoredObjectAsync(storage, options.Value, file.ObjectKey, ct);
        if (sourceBucket is null)
        {
            await RescueStuckScanHandler.AbandonAsync(
                file,
                RescueStuckScanHandler.BlobMissingReason,
                "blob_missing: stored object not found during scan",
                "scan",
                command.CorrelationId,
                limits,
                audit,
                clock,
                unitOfWork,
                bus,
                ct
            );
            logger.LogWarning("Scan of file {FileId} found no stored object; marked blob_missing.", file.Id);
            return;
        }

        // Mover o borrar el objeto viaja en un comando que sale después de guardar el veredicto.
        RelocateScannedBlobCommand? RelocateTo(string? targetBucket) =>
            targetBucket == sourceBucket
                ? null
                : new RelocateScannedBlobCommand(
                    command.TenantId,
                    file.Id,
                    file.ObjectKey,
                    sourceBucket,
                    targetBucket,
                    file.CreatedBy,
                    command.CorrelationId
                );
        RelocateScannedBlobCommand? relocation = null;

        var tempPath = Path.Combine(Path.GetTempPath(), $"taxvision-scan-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (
                var destination = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                )
            )
            {
                await storage.DownloadAsync(sourceBucket, file.ObjectKey, destination, ct);
            }

            await using var content = new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );

            var inspected = await inspector.InspectAsync(content, file.OriginalName, ct);
            var limit =
                await limits.GetAsync(command.TenantId, ct)
                ?? throw new InvalidOperationException("Tenant quota projection is missing.");
            var uploadPolicy = await uploadPolicies.ResolveAsync(
                command.TenantId,
                limit,
                file.FolderType,
                forCustomerPortal: false,
                ct
            );
            if (
                !inspected.IsSafe
                || !FileTypeCompatibility.Matches(file.OriginalName, inspected.ContentType)
                || !uploadPolicy.AllowedContentTypes.Contains(inspected.ContentType, StringComparer.OrdinalIgnoreCase)
            )
            {
                file.MarkScanFailed(inspected.RejectionReason ?? "Detected content type is not allowed.", clock.UtcNow);
                (await limits.GetAsync(command.TenantId, ct))?.Release(file.SizeBytes);
                audit.Add(
                    StorageAccessLog.Create(
                        command.TenantId,
                        file.Id,
                        file.CreatedBy,
                        "scan",
                        "rejected",
                        null,
                        null,
                        command.CorrelationId,
                        file.ScanReport,
                        clock.UtcNow
                    )
                );
                await bus.PublishAsync(
                    new FileScanFailedIntegrationEvent
                    {
                        TenantId = command.TenantId,
                        FileId = file.Id,
                        ObjectKey = file.ObjectKey,
                        Reason = "content_type_mismatch",
                        OwnerType = file.OwnerType.ToString(),
                        OwnerId = file.OwnerId,
                        FolderType = file.FolderType.ToString(),
                        CreatedBy = file.CreatedBy,
                        CorrelationId = command.CorrelationId,
                    }
                );
                await unitOfWork.SaveChangesAsync(ct);
                await PublishRelocationAsync(bus, RelocateTo(null));
                return;
            }

            content.Position = 0;
            var scan = await virusScanner.ScanAsync(content, ct);
            if (scan.Verdict == VirusScanVerdict.Clean)
            {
                // ClamAV solo mira virus/malware — IContentScanner es la pasada aparte de
                // moderacion de contenido (T-20); la politica en BD decide si aplica.
                content.Position = 0;
                var contentScan = await contentScanner.ScanAsync(
                    content,
                    new ContentScanContext(
                        command.TenantId,
                        file.Id,
                        file.OwnerType,
                        file.OriginalName,
                        inspected.ContentType,
                        file.FolderType
                    ),
                    ct
                );
                if (contentScan.Verdict != ContentScanVerdict.Clean)
                {
                    limit.Release(file.SizeBytes);
                    var reason = contentScan.Reason ?? "Content policy verdict without detail.";

                    if (contentScan.Verdict == ContentScanVerdict.PolicyViolation)
                    {
                        file.MarkBlockedByPolicy(reason, inspected.ContentType, clock.UtcNow);
                        // Se conserva como evidencia (T-20 §6): el purgador de la papelera salta lo retenido.
                        if (!file.IsLegalHeld)
                            file.PlaceLegalHold();
                        audit.Add(
                            StorageAccessLog.Create(
                                command.TenantId,
                                file.Id,
                                file.CreatedBy,
                                "scan",
                                "blocked-by-policy",
                                null,
                                null,
                                command.CorrelationId,
                                reason,
                                clock.UtcNow
                            )
                        );
                        await bus.PublishAsync(
                            new FileBlockedByPolicyIntegrationEvent
                            {
                                TenantId = command.TenantId,
                                FileId = file.Id,
                                ObjectKey = file.ObjectKey,
                                PolicyReason = reason,
                                CreatedBy = file.CreatedBy,
                                OwnerType = file.OwnerType.ToString(),
                                OwnerId = file.OwnerId,
                                FolderType = file.FolderType.ToString(),
                                CorrelationId = command.CorrelationId,
                            }
                        );
                    }
                    else
                    {
                        var reviewChecksum = ChecksumSha256.Create(inspected.Sha256);
                        if (reviewChecksum.IsFailure)
                            throw new InvalidOperationException(reviewChecksum.Error.Message);
                        file.MarkPendingReview(reason, inspected.ContentType, reviewChecksum.Value, clock.UtcNow);
                        audit.Add(
                            StorageAccessLog.Create(
                                command.TenantId,
                                file.Id,
                                file.CreatedBy,
                                "scan",
                                "pending-review",
                                null,
                                null,
                                command.CorrelationId,
                                reason,
                                clock.UtcNow
                            )
                        );
                        await bus.PublishAsync(
                            new FilePendingReviewIntegrationEvent
                            {
                                TenantId = command.TenantId,
                                FileId = file.Id,
                                ObjectKey = file.ObjectKey,
                                Reason = reason,
                                CreatedBy = file.CreatedBy,
                                OwnerType = file.OwnerType.ToString(),
                                OwnerId = file.OwnerId,
                                FolderType = file.FolderType.ToString(),
                                CorrelationId = command.CorrelationId,
                            }
                        );
                    }
                    await unitOfWork.SaveChangesAsync(ct);
                    await PublishRelocationAsync(bus, RelocateTo(options.Value.QuarantineBucket));
                    return;
                }

                var checksum = ChecksumSha256.Create(inspected.Sha256);
                if (checksum.IsFailure)
                    throw new InvalidOperationException(checksum.Error.Message);

                if (sourceBucket != options.Value.MainBucket)
                {
                    await storage.CopyAsync(sourceBucket, file.ObjectKey, options.Value.MainBucket, ct);
                    await storage.DeleteAsync(sourceBucket, file.ObjectKey, ct);
                }
                // Avatar: la etiqueta activa la regla de lifecycle que expira sus versiones no actuales
                // (bucket versionado). Se aplica también en el reintento que ya lo encontró en Main.
                if (file.FolderType == FolderType.Avatars)
                    await storage.SetObjectTagsAsync(
                        options.Value.MainBucket,
                        file.ObjectKey,
                        StorageObjectTags.Avatar,
                        ct
                    );
                file.MarkAvailable(checksum.Value, inspected.ContentType, clock.UtcNow);
                limit.Commit(file.SizeBytes);
                audit.Add(
                    StorageAccessLog.Create(
                        command.TenantId,
                        file.Id,
                        file.CreatedBy,
                        "scan",
                        "clean",
                        null,
                        null,
                        command.CorrelationId,
                        null,
                        clock.UtcNow
                    )
                );
                await bus.PublishAsync(
                    new FileAvailableIntegrationEvent
                    {
                        TenantId = command.TenantId,
                        FileId = file.Id,
                        ObjectKey = file.ObjectKey,
                        ContentType = inspected.ContentType,
                        SizeBytes = file.SizeBytes,
                        ChecksumSha256 = inspected.Sha256,
                        CreatedBy = file.CreatedBy,
                        OwnerType = file.OwnerType.ToString(),
                        OwnerId = file.OwnerId,
                        FolderId = file.FolderId,
                        FolderType = file.FolderType.ToString(),
                        OriginalName = file.OriginalName,
                        TaxYear = file.TaxYear,
                        CorrelationId = command.CorrelationId,
                    }
                );
                // Ni Client Forms ni una foto de perfil son "el cliente subió un documento".
                if (
                    command.PortalCustomerId is { } customerId
                    && file.FolderType is not (FolderType.ClientForms or FolderType.Avatars)
                )
                {
                    await bus.PublishAsync(
                        new CustomerDocumentUploadedIntegrationEvent
                        {
                            TenantId = command.TenantId,
                            CustomerId = customerId,
                            FileId = file.Id,
                            FolderId = file.FolderId,
                            CorrelationId = command.CorrelationId,
                        }
                    );
                }
            }
            else if (scan.Verdict == VirusScanVerdict.Infected)
            {
                relocation = RelocateTo(options.Value.QuarantineBucket);
                file.MarkInfected(scan.Report, inspected.ContentType, clock.UtcNow);
                limit.Release(file.SizeBytes);
                audit.Add(
                    StorageAccessLog.Create(
                        command.TenantId,
                        file.Id,
                        file.CreatedBy,
                        "scan",
                        "infected",
                        null,
                        null,
                        command.CorrelationId,
                        scan.Report,
                        clock.UtcNow
                    )
                );
                await bus.PublishAsync(
                    new FileInfectedDetectedIntegrationEvent
                    {
                        TenantId = command.TenantId,
                        FileId = file.Id,
                        ObjectKey = file.ObjectKey,
                        ScanReport = scan.Report,
                        OwnerType = file.OwnerType.ToString(),
                        OwnerId = file.OwnerId,
                        FolderType = file.FolderType.ToString(),
                        CorrelationId = command.CorrelationId,
                    }
                );
            }
            else
            {
                throw new VirusScanUnavailableException($"ClamAV could not produce a verdict: {scan.Report}");
            }

            await unitOfWork.SaveChangesAsync(ct);
            await PublishRelocationAsync(bus, relocation);
        }
        // Sin catch: un fallo transitorio (ClamAV, MinIO, ContentScanner) sube a Wolverine, que reintenta y
        // luego lo manda a la DLQ; el archivo conserva su reserva y StuckScanRescueService lo recoge.
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>Busca el objeto en temp → main → cuarentena (un intento anterior pudo moverlo); null si no está.</summary>
    internal static async Task<string?> LocateStoredObjectAsync(
        IObjectStorage storage,
        CloudStorageOptions options,
        string objectKey,
        CancellationToken ct
    )
    {
        foreach (var bucket in new[] { options.TempBucket, options.MainBucket, options.QuarantineBucket })
        {
            if (await storage.ExistsAsync(bucket, objectKey, ct))
                return bucket;
        }
        return null;
    }

    private static async Task PublishRelocationAsync(IMessageBus bus, RelocateScannedBlobCommand? relocation)
    {
        if (relocation is not null)
            await bus.PublishAsync(relocation);
    }
}

public sealed record DeleteFileCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid FileId,
    RequestAuditContext Audit
);

public static class DeleteFileHandler
{
    public static async Task<Result> Handle(
        DeleteFileCommand command,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        IStoragePlatformSettingsProvider platform,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
            return Result.Failure(FileErrors.NotFound);
        if (!command.Scope.CanAccess(file))
            return Result.Failure(FileErrors.NotFound);
        // Portal con Delete deny (defensa en profundidad — el portal no borra, pero si llegara, 403 explícito).
        if (command.Scope.IsDeniedForPortal(AccessResourceType.File, file.Id, AccessCapability.Delete))
            return Result.Failure(
                CustomerAccessDenied.With(
                    command.Scope.DenyReasonFor(AccessResourceType.File, file.Id, AccessCapability.Delete)
                )
            );

        var retentionDays = (await platform.GetAsync(ct)).RecycleBinRetentionDays;
        var result = file.SoftDelete(clock.UtcNow, TimeSpan.FromDays(retentionDays));
        if (result.IsFailure)
            return result;

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                file.Id,
                command.ActorId,
                "delete.soft",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                $"retention-days={retentionDays}",
                clock.UtcNow
            )
        );
        await bus.PublishAsync(
            new FileDeletedIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = file.Id,
                CreatedBy = file.CreatedBy,
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await bus.PublishAsync(
            new FileAccessAuditedIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = file.Id,
                ActorId = command.ActorId,
                Action = "delete.soft",
                Outcome = "success",
                IpAddress = command.Audit.IpAddress,
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
