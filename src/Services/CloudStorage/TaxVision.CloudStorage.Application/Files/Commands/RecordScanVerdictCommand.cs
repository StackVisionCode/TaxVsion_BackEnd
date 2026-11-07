using BuildingBlocks.Persistence;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Commands;

/// <summary>
/// El veredicto de un escaneo ya hecho. Lo invoca <see cref="ScanFileHandler"/> en línea mientras sostiene el
/// turno; <paramref name="SourceBucket"/> null significa que no encontró el objeto.
/// </summary>
public sealed record RecordScanVerdictCommand(
    Guid TenantId,
    Guid FileId,
    string CorrelationId,
    Guid? PortalCustomerId,
    string? SourceBucket,
    ScanFindings? Findings,
    string? Report,
    string? ContentType,
    string? Sha256
)
{
    public static RecordScanVerdictCommand BlobMissing(ScanFileCommand scan) =>
        new(scan.TenantId, scan.FileId, scan.CorrelationId, scan.PortalCustomerId, null, null, null, null, null);
}

/// <summary>
/// Aplica lo que decide <see cref="ScanVerdictPolicy"/> en una transacción corta. Idempotente: solo registra sobre
/// un archivo que sigue en Scanning; si ganó un borrado o un intento anterior, no hace nada.
/// </summary>
public static class RecordScanVerdictHandler
{
    private const string ContentTypeMismatchReason = "content_type_mismatch";

    public static async Task Handle(
        RecordScanVerdictCommand command,
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
        if (file is not { Status: FileStatus.Scanning })
            return;

        if (command.SourceBucket is not { } sourceBucket || command.Findings is not { } findings)
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
                ct
            );
            return;
        }

        var limit =
            await limits.GetAsync(command.TenantId, ct)
            ?? throw new InvalidOperationException("Tenant quota projection is missing.");
        var decision = ScanVerdictPolicy.Decide(findings);

        await ApplyDecisionAsync(decision, command, file, sourceBucket, storage, options.Value, clock, ct);

        if (decision.Quota == ScanQuotaEffect.Commit)
            limit.Commit(file.SizeBytes);
        else
            limit.Release(file.SizeBytes);

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                file.Id,
                file.CreatedBy,
                "scan",
                decision.AuditOutcome,
                null,
                null,
                command.CorrelationId,
                command.Report,
                clock.UtcNow
            )
        );

        await unitOfWork.SaveChangesAsync(ct);

        // Mover o borrar el objeto viaja en un comando que sale después de guardar el veredicto.
        if (Relocation(decision, command, file, sourceBucket, options.Value) is { } relocation)
            await bus.PublishAsync(relocation);
    }

    private static async Task ApplyDecisionAsync(
        ScanDecision decision,
        RecordScanVerdictCommand command,
        FileObject file,
        string sourceBucket,
        IObjectStorage storage,
        CloudStorageOptions options,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        var correlationId = command.CorrelationId;
        var report = command.Report;
        var contentType = command.ContentType!;
        switch (decision.Outcome)
        {
            case ScanOutcome.ContentRejected:
                file.MarkScanFailed(report!, clock.UtcNow, ContentTypeMismatchReason, correlationId);
                break;

            case ScanOutcome.Infected:
                file.MarkInfected(report!, contentType, clock.UtcNow, correlationId);
                break;

            case ScanOutcome.BlockedByPolicy:
                file.MarkBlockedByPolicy(report!, contentType, clock.UtcNow, correlationId);
                if (decision.RetainAsEvidence && !file.IsLegalHeld)
                    file.PlaceLegalHold();
                break;

            case ScanOutcome.PendingReview:
                file.MarkPendingReview(report!, contentType, ChecksumOf(command), clock.UtcNow, correlationId);
                break;

            default:
                var checksum = ChecksumOf(command);
                await MoveToMainBucketAsync(file, sourceBucket, storage, options, ct);
                file.MarkAvailable(checksum, contentType, clock.UtcNow, correlationId, command.PortalCustomerId);
                break;
        }
    }

    private static ChecksumSha256 ChecksumOf(RecordScanVerdictCommand command)
    {
        var checksum = ChecksumSha256.Create(command.Sha256!);
        return checksum.IsSuccess ? checksum.Value : throw new InvalidOperationException(checksum.Error.Message);
    }

    private static async Task MoveToMainBucketAsync(
        FileObject file,
        string sourceBucket,
        IObjectStorage storage,
        CloudStorageOptions options,
        CancellationToken ct
    )
    {
        if (sourceBucket != options.MainBucket)
        {
            await storage.CopyAsync(sourceBucket, file.ObjectKey, options.MainBucket, ct);
            await storage.DeleteAsync(sourceBucket, file.ObjectKey, ct);
        }
        // Avatar: la etiqueta activa la regla de lifecycle que expira sus versiones no actuales
        // (bucket versionado). Se aplica también en el reintento que ya lo encontró en Main.
        if (file.FolderType == FolderType.Avatars)
            await storage.SetObjectTagsAsync(options.MainBucket, file.ObjectKey, StorageObjectTags.Avatar, ct);
    }

    private static RelocateScannedBlobCommand? Relocation(
        ScanDecision decision,
        RecordScanVerdictCommand command,
        FileObject file,
        string sourceBucket,
        CloudStorageOptions options
    )
    {
        var targetBucket = decision.Target switch
        {
            ScanTarget.Quarantine => options.QuarantineBucket,
            ScanTarget.Main => sourceBucket,
            _ => null,
        };
        return targetBucket == sourceBucket
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
    }
}
