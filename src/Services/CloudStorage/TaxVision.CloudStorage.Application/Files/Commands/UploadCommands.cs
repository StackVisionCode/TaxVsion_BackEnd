using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Commands;

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
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectKeyBuilder keyBuilder,
        IObjectStorage storage,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var request = command.Request;
        if (!command.Scope.CanCreate(request.OwnerType, request.OwnerId))
            return Result.Failure<InitiatedUploadResponse>(FileErrors.Forbidden);

        var extension = Path.GetExtension(request.OriginalName).ToLowerInvariant();
        var config = options.Value;

        var limit = await limits.GetAsync(command.TenantId, ct);
        if (limit is null)
            return Result.Failure<InitiatedUploadResponse>(QuotaErrors.NotProvisioned);

        var policy = config.ResolvePlanPolicy(limit.PlanCode);
        if (
            string.IsNullOrWhiteSpace(request.OriginalName)
            || request.OriginalName.Length > 255
            || !policy.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || !policy.AllowedContentTypes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase)
        )
            return Result.Failure<InitiatedUploadResponse>(FileErrors.UnsupportedType);

        var reserve = limit.Reserve(request.SizeBytes);
        if (reserve.IsFailure)
        {
            if (reserve.Error == QuotaErrors.Exceeded)
            {
                await bus.PublishAsync(
                    new StorageLimitExceededIntegrationEvent
                    {
                        TenantId = command.TenantId,
                        AttemptedFileSizeBytes = request.SizeBytes,
                        UsedBytes = limit.UsedBytes,
                        ReservedBytes = limit.ReservedBytes,
                        MaxBytes = limit.MaxBytes,
                        CorrelationId = command.Audit.CorrelationId,
                    }
                );
            }
            return Result.Failure<InitiatedUploadResponse>(reserve.Error);
        }

        var fileId = Guid.NewGuid();
        var key = keyBuilder.Build(
            fileId,
            command.TenantId,
            request.OwnerType,
            request.OwnerId,
            request.FolderType,
            request.TaxYear,
            request.OriginalName
        );
        if (key.IsFailure)
        {
            limit.Release(request.SizeBytes);
            return Result.Failure<InitiatedUploadResponse>(key.Error);
        }

        var registered = FileObject.Register(
            fileId,
            command.TenantId,
            request.OwnerType,
            request.OwnerId,
            request.FolderType,
            request.TaxYear,
            key.Value,
            Path.GetFileName(request.OriginalName),
            request.ContentType,
            request.SizeBytes,
            command.ActorId,
            clock.UtcNow,
            clock.UtcNow.AddHours(Math.Clamp(config.UploadReservationHours, 1, 168))
        );
        if (registered.IsFailure)
        {
            limit.Release(request.SizeBytes);
            return Result.Failure<InitiatedUploadResponse>(registered.Error);
        }

        files.Add(registered.Value);
        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                fileId,
                command.ActorId,
                "upload.initiated",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                $"size={request.SizeBytes}",
                clock.UtcNow
            )
        );

        var lifetime = TimeSpan.FromMinutes(Math.Clamp(config.PresignedUrlMinutes, 1, 60));
        var upload = await storage.CreateUploadPolicyAsync(
            config.TempBucket,
            key.Value.Value,
            request.ContentType,
            request.SizeBytes,
            lifetime,
            ct
        );
        await bus.PublishAsync(
            new FileAccessAuditedIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = fileId,
                ActorId = command.ActorId,
                Action = "upload.initiated",
                Outcome = "success",
                IpAddress = command.Audit.IpAddress,
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new InitiatedUploadResponse(
                fileId,
                upload.Url,
                upload.FormData,
                clock.UtcNow.Add(lifetime),
                registered.Value.Status
            )
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

        var transition = file.MarkPendingScan();
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
        await bus.PublishAsync(new ScanFileCommand(command.TenantId, file.Id, command.Audit.CorrelationId));
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

public sealed record ScanFileCommand(Guid TenantId, Guid FileId, string CorrelationId);

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
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var file =
            await files.GetAsync(command.TenantId, command.FileId, ct)
            ?? throw new InvalidOperationException($"File {command.FileId} does not exist.");

        // Terminales — un redelivery de ScanFileCommand (retry de Wolverine tras
        // un crash post-transition-pre-ack) no debe relanzar el scan.
        if (
            file.Status
            is FileStatus.Available
                or FileStatus.Infected
                or FileStatus.BlockedByPolicy
                or FileStatus.PendingReview
        )
            return;

        var transition = file.MarkScanning();
        if (transition.IsFailure)
            throw new InvalidOperationException(transition.Error.Message);
        await unitOfWork.SaveChangesAsync(ct);

        var sourceBucket = options.Value.TempBucket;
        if (!await storage.ExistsAsync(sourceBucket, file.ObjectKey, ct))
        {
            // Un intento anterior pudo mover el objeto y perder después una concurrencia
            // optimista al confirmar la cuota. Continuar desde el bucket principal hace
            // que el reintento sea idempotente.
            sourceBucket = options.Value.MainBucket;
            if (!await storage.ExistsAsync(sourceBucket, file.ObjectKey, ct))
                throw new InvalidOperationException($"Stored object for file {file.Id} does not exist.");
        }

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
            var planPolicy = options.Value.ResolvePlanPolicy(limit.PlanCode);
            if (
                !inspected.IsSafe
                || !FileTypeCompatibility.Matches(file.OriginalName, inspected.ContentType)
                || !planPolicy.AllowedContentTypes.Contains(inspected.ContentType, StringComparer.OrdinalIgnoreCase)
            )
            {
                file.MarkScanFailed(inspected.RejectionReason ?? "Detected content type is not allowed.", clock.UtcNow);
                (await limits.GetAsync(command.TenantId, ct))?.Release(file.SizeBytes);
                await storage.DeleteAsync(sourceBucket, file.ObjectKey, ct);
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
                await unitOfWork.SaveChangesAsync(ct);
                return;
            }

            content.Position = 0;
            var scan = await virusScanner.ScanAsync(content, ct);
            if (scan.Verdict == VirusScanVerdict.Clean)
            {
                // ClamAV solo mira virus/malware — IContentScanner es una pasada
                // aparte para moderacion de contenido (NSFW/CSAM/politica). Se
                // corre SIEMPRE, no solo cuando hay un scanner real conectado:
                // el NoOp de MVP siempre da Clean, asi que este bloque es
                // transparente hasta que se enchufe una implementacion real.
                content.Position = 0;
                var contentScan = await contentScanner.ScanAsync(
                    content,
                    new ContentScanContext(command.TenantId, file.Id, file.OwnerType, file.OriginalName),
                    ct
                );
                if (contentScan.Verdict != ContentScanVerdict.Clean)
                {
                    await storage.CopyAsync(sourceBucket, file.ObjectKey, options.Value.QuarantineBucket, ct);
                    await storage.DeleteAsync(sourceBucket, file.ObjectKey, ct);
                    limit.Release(file.SizeBytes);
                    var reason = contentScan.Reason ?? "Content policy verdict without detail.";

                    if (contentScan.Verdict == ContentScanVerdict.PolicyViolation)
                    {
                        file.MarkBlockedByPolicy(reason, inspected.ContentType, clock.UtcNow);
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
                                CorrelationId = command.CorrelationId,
                            }
                        );
                    }
                    else
                    {
                        file.MarkPendingReview(reason, inspected.ContentType, clock.UtcNow);
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
                                CorrelationId = command.CorrelationId,
                            }
                        );
                    }
                    await unitOfWork.SaveChangesAsync(ct);
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
                        CorrelationId = command.CorrelationId,
                    }
                );
            }
            else if (scan.Verdict == VirusScanVerdict.Infected)
            {
                await storage.CopyAsync(sourceBucket, file.ObjectKey, options.Value.QuarantineBucket, ct);
                await storage.DeleteAsync(sourceBucket, file.ObjectKey, ct);
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
                        CorrelationId = command.CorrelationId,
                    }
                );
            }
            else
            {
                throw new InvalidOperationException($"ClamAV could not produce a verdict: {scan.Report}");
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
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

        var result = file.SoftDelete(clock.UtcNow, TimeSpan.FromDays(30));
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
                "retention-days=30",
                clock.UtcNow
            )
        );
        await bus.PublishAsync(
            new FileDeletedIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = file.Id,
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
