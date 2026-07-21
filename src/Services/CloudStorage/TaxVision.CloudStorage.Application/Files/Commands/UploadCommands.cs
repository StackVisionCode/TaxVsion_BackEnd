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
        var config = options.Value;

        var registered = await UploadRegistration.ReserveAndRegisterAsync(
            command.TenantId,
            command.ActorId,
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
            config,
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
                $"size={request.SizeBytes}",
                clock.UtcNow
            )
        );

        var lifetime = TimeSpan.FromMinutes(Math.Clamp(config.PresignedUrlMinutes, 1, 60));
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
        ILogger<ScanFileCommand> logger,
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
            var uploadPolicy = options.Value.ResolveUploadPolicy(limit.PlanCode, file.FolderType);
            if (
                !inspected.IsSafe
                || !FileTypeCompatibility.Matches(file.OriginalName, inspected.ContentType)
                || !uploadPolicy.AllowedContentTypes.Contains(inspected.ContentType, StringComparer.OrdinalIgnoreCase)
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
                                CreatedBy = file.CreatedBy,
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
                        CreatedBy = file.CreatedBy,
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Este handler corre en background via Wolverine — sin ningun
            // middleware HTTP que atrape la excepcion, un fallo transitorio
            // (I/O de red, race del SDK de MinIO al copiar el stream
            // descargado, etc.) tumbaba el proceso ENTERO de CloudStorage en
            // vez de fallar solo este archivo. Se marca ScanFailed (no es
            // terminal — ver el guard de redelivery arriba, un redelivery
            // reintenta el scan) y se libera la reserva de cuota con
            // CancellationToken.None por si `ct` ya esta cancelado.
            logger.LogError(
                ex,
                "Unexpected error scanning file {FileId} for tenant {TenantId}.",
                file.Id,
                command.TenantId
            );
            try
            {
                var limit = await limits.GetAsync(command.TenantId, CancellationToken.None);
                limit?.Release(file.SizeBytes);
                file.MarkScanFailed($"Unexpected error: {ex.GetType().Name}: {ex.Message}", clock.UtcNow);
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception recoveryEx)
            {
                logger.LogError(recoveryEx, "Failed to mark file {FileId} as ScanFailed after scan error.", file.Id);
            }
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

        var retentionDays = options.Value.RecycleBinRetentionDays;
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
