using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
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
        await unitOfWork.SaveChangesAsync(ct);
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
        await unitOfWork.SaveChangesAsync(ct);
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
        return Result.Success();
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
        await unitOfWork.SaveChangesAsync(ct);
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
        return Result.Success();
    }
}
