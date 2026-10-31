using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Commands;

public sealed record CopyFileCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid SourceFileId,
    CopyFileRequest Request,
    RequestAuditContext Audit
);

public static class CopyFileHandler
{
    public static async Task<Result<CopiedFileResponse>> Handle(
        CopyFileCommand command,
        IFileObjectRepository files,
        IFolderRepository folders,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectKeyBuilder keyBuilder,
        ISystemFolderProvisioner systemFolders,
        IObjectStorage storage,
        IOptions<CloudStorageOptions> options,
        IUploadPolicyResolver uploadPolicies,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var source = await files.GetAsync(command.TenantId, command.SourceFileId, ct);
        if (source is null || !command.Scope.CanAccess(source))
            return Result.Failure<CopiedFileResponse>(FileErrors.NotFound);
        if (source.Status != FileStatus.Available)
            return Result.Failure<CopiedFileResponse>(FileErrors.NotAvailable);

        var destination = await ResolveDestinationAsync(command, folders, systemFolders, options.Value, clock, ct);
        if (destination.IsFailure)
            return Result.Failure<CopiedFileResponse>(destination.Error);

        var registered = await RegisterCopyAsync(
            command,
            source,
            limits,
            keyBuilder,
            systemFolders,
            options.Value,
            uploadPolicies,
            clock,
            bus,
            ct
        );
        if (registered.IsFailure)
            return Result.Failure<CopiedFileResponse>(registered.Error);

        var copy = registered.Value;
        if (destination.Value is { } folderId)
            copy.MoveToFolder(folderId, clock.UtcNow);

        await storage.CopyAsync(
            options.Value.MainBucket,
            source.ObjectKey,
            options.Value.TempBucket,
            copy.ObjectKey,
            ct
        );
        var pendingScan = copy.MarkPendingScan();
        if (pendingScan.IsFailure)
            return Result.Failure<CopiedFileResponse>(pendingScan.Error);

        files.Add(copy);
        audit.Add(CreateAudit(command, source, copy, clock.UtcNow));
        await bus.PublishAsync(new ScanFileCommand(command.TenantId, copy.Id, command.Audit.CorrelationId));
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new CopiedFileResponse(copy.Id, copy.Status));
    }

    private static async Task<Result<Guid?>> ResolveDestinationAsync(
        CopyFileCommand command,
        IFolderRepository folders,
        ISystemFolderProvisioner systemFolders,
        CloudStorageOptions options,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        var request = command.Request;
        if (!command.Scope.CanCreate(request.OwnerType, request.OwnerId))
            return Result.Failure<Guid?>(FileErrors.Forbidden);

        // Client Forms solo recibe cargas del formulario: copiar hacia ahí lo saltaría.
        if (request.FolderType == FolderType.ClientForms)
            return Result.Failure<Guid?>(FileErrors.ClientFormsDestinationNotAllowed);

        if (request.FolderId is { } requestedFolderId)
        {
            var folder = await folders.GetAsync(command.TenantId, requestedFolderId, ct);
            if (folder is null)
                return Result.Failure<Guid?>(FolderErrors.NotFound);
            if (folder.OwnerType != request.OwnerType || folder.OwnerId != request.OwnerId)
                return Result.Failure<Guid?>(FolderErrors.OwnerMismatch);
            if (IsClientFormsFolder(folder))
                return Result.Failure<Guid?>(FileErrors.ClientFormsDestinationNotAllowed);
            return Result.Success<Guid?>(folder.Id);
        }

        if (!options.AutoSystemFolders)
            return Result.Success<Guid?>(null);

        var folderId = await systemFolders.ResolveFolderIdAsync(
            command.TenantId,
            request.OwnerType,
            request.OwnerId,
            request.FolderType,
            command.ActorId,
            clock.UtcNow,
            ct
        );
        return Result.Success(folderId);
    }

    private static bool IsClientFormsFolder(Folder folder) =>
        SystemFolderCatalog.TryGet(FolderType.ClientForms, out var spec)
        && string.Equals(folder.Category, spec.Category, StringComparison.Ordinal);

    private static Task<Result<FileObject>> RegisterCopyAsync(
        CopyFileCommand command,
        FileObject source,
        IStorageLimitRepository limits,
        IObjectKeyBuilder keyBuilder,
        ISystemFolderProvisioner systemFolders,
        CloudStorageOptions options,
        IUploadPolicyResolver uploadPolicies,
        ISystemClock clock,
        IMessageBus bus,
        CancellationToken ct
    ) =>
        UploadRegistration.ReserveAndRegisterAsync(
            command.TenantId,
            command.ActorId,
            command.Scope,
            command.Request.OwnerType,
            command.Request.OwnerId,
            command.Request.FolderType,
            command.Request.TaxYear,
            source.OriginalName,
            source.DetectedContentType ?? source.DeclaredContentType,
            source.SizeBytes,
            command.Audit.CorrelationId,
            limits,
            keyBuilder,
            systemFolders,
            options,
            uploadPolicies,
            clock,
            bus,
            ct
        );

    private static StorageAccessLog CreateAudit(
        CopyFileCommand command,
        FileObject source,
        FileObject copy,
        DateTime nowUtc
    ) =>
        StorageAccessLog.Create(
            command.TenantId,
            copy.Id,
            command.ActorId,
            "file.copied",
            "pending-scan",
            command.Audit.IpAddress,
            command.Audit.UserAgent,
            command.Audit.CorrelationId,
            $"sourceFileId={source.Id}",
            nowUtc
        );
}
