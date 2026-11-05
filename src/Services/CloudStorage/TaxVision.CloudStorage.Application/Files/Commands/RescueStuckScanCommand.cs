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

/// <summary>Lo despacha StuckScanRescueService, un archivo por invocación (su propia transacción y outbox).</summary>
public sealed record RescueStuckScanCommand(Guid TenantId, Guid FileId);

public static class RescueStuckScanHandler
{
    public const string BlobMissingReason = "blob_missing";
    public const string ScanStalledReason = "scan_stalled";

    public static async Task Handle(
        RescueStuckScanCommand command,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        IObjectStorage storage,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<RescueStuckScanCommand> logger,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null || file.Status is not (FileStatus.PendingScan or FileStatus.Scanning))
            return;

        var config = options.Value;
        var correlationId = $"scan-rescue-{file.Id:N}";
        var blobFound = await ScanFileHandler.LocateStoredObjectAsync(storage, config, file.ObjectKey, ct) is not null;
        if (!blobFound)
        {
            await AbandonAsync(
                file,
                BlobMissingReason,
                "blob_missing: stored object not found during scan rescue",
                "scan.rescue",
                correlationId,
                limits,
                audit,
                clock,
                unitOfWork,
                bus,
                ct
            );
            logger.LogWarning("Stuck scan {FileId} has no stored object; marked blob_missing.", file.Id);
            return;
        }

        var requeued = file.RequeueScan(clock.UtcNow, config.MaxScanAttempts);
        if (requeued.IsFailure)
        {
            if (requeued.Error != FileErrors.ScanAttemptsExhausted)
                return;
            await AbandonAsync(
                file,
                ScanStalledReason,
                $"scan_stalled: scan did not finish after {file.ScanAttemptCount} requeues",
                "scan.rescue",
                correlationId,
                limits,
                audit,
                clock,
                unitOfWork,
                bus,
                ct
            );
            logger.LogWarning("Stuck scan {FileId} exhausted its requeues; marked scan_stalled.", file.Id);
            return;
        }

        audit.Add(
            StorageAccessLog.Create(
                file.TenantId,
                file.Id,
                file.CreatedBy,
                "scan.rescue",
                "requeued",
                null,
                null,
                correlationId,
                $"attempt={file.ScanAttemptCount}",
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new ScanFileCommand(file.TenantId, file.Id, correlationId));
    }

    /// <summary>ScanFailed visible + reserva liberada + FileScanFailed; también lo usa ScanFileHandler (CST-3).</summary>
    internal static async Task AbandonAsync(
        FileObject file,
        string reason,
        string report,
        string auditAction,
        string correlationId,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (file.MarkScanAbandoned(report, clock.UtcNow).IsFailure)
            return;
        (await limits.GetAsync(file.TenantId, ct))?.Release(file.SizeBytes);
        audit.Add(
            StorageAccessLog.Create(
                file.TenantId,
                file.Id,
                file.CreatedBy,
                auditAction,
                "irrecoverable",
                null,
                null,
                correlationId,
                report,
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new FileScanFailedIntegrationEvent
            {
                TenantId = file.TenantId,
                FileId = file.Id,
                ObjectKey = file.ObjectKey,
                Reason = reason,
                OwnerType = file.OwnerType.ToString(),
                OwnerId = file.OwnerId,
                FolderType = file.FolderType.ToString(),
                CreatedBy = file.CreatedBy,
                CorrelationId = correlationId,
            }
        );
    }
}
