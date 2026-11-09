using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Platform;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Moderation;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Moderation;

public sealed record ContentReviewItemView(
    Guid TenantId,
    Guid FileId,
    string Status,
    string OriginalName,
    string? DetectedContentType,
    long SizeBytes,
    string FolderType,
    string OwnerType,
    Guid? OwnerId,
    Guid CreatedBy,
    DateTime CreatedAtUtc,
    DateTime? ScannedAtUtc,
    string? ScanReport,
    bool IsLegalHeld
)
{
    public static ContentReviewItemView From(FileObject file) =>
        new(
            file.TenantId,
            file.Id,
            file.Status.ToString(),
            file.OriginalName,
            file.DetectedContentType,
            file.SizeBytes,
            file.FolderType.ToString(),
            file.OwnerType.ToString(),
            file.OwnerId,
            file.CreatedBy,
            file.CreatedAtUtc,
            file.ScannedAtUtc,
            file.ScanReport,
            file.IsLegalHeld
        );
}

/// <summary>Cola de PlatformAdmin: PendingReview (por decidir) o BlockedByPolicy (ya bloqueados).</summary>
public sealed record ListContentReviewQueueQuery(FileStatus Status, int Skip, int Take);

public static class ListContentReviewQueueHandler
{
    public static async Task<Result<IReadOnlyList<ContentReviewItemView>>> Handle(
        ListContentReviewQueueQuery query,
        IContentReviewQueue queue,
        CancellationToken ct
    )
    {
        if (query.Status is not (FileStatus.PendingReview or FileStatus.BlockedByPolicy))
            return Result.Failure<IReadOnlyList<ContentReviewItemView>>(ContentModerationErrors.NotUnderModeration);
        var files = await queue.ListAsync(query.Status, Math.Max(0, query.Skip), Math.Clamp(query.Take, 1, 200), ct);
        return Result.Success<IReadOnlyList<ContentReviewItemView>>(files.Select(ContentReviewItemView.From).ToList());
    }
}

public sealed record ContentReviewPreviewView(Uri Url, DateTime ExpiresAtUtc, string? ContentType);

/// <summary>URL firmada corta e inline para que el revisor vea el archivo; cada vista queda auditada.</summary>
public sealed record PreviewContentReviewFileCommand(
    Guid TenantId,
    Guid FileId,
    Guid ActorId,
    RequestAuditContext Audit
);

public static class PreviewContentReviewFileHandler
{
    public static async Task<Result<ContentReviewPreviewView>> Handle(
        PreviewContentReviewFileCommand command,
        IFileObjectRepository files,
        IObjectStorage storage,
        IStorageAuditRepository audit,
        IOptions<CloudStorageOptions> options,
        IStoragePlatformSettingsProvider platform,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
            return Result.Failure<ContentReviewPreviewView>(FileErrors.NotFound);
        if (file.Status is not (FileStatus.PendingReview or FileStatus.BlockedByPolicy))
            return Result.Failure<ContentReviewPreviewView>(ContentModerationErrors.NotUnderModeration);

        // Lo del scanner vive en cuarentena; un bloqueo DMCA deja el objeto en el bucket principal.
        var bucket = await storage.ExistsAsync(options.Value.QuarantineBucket, file.ObjectKey, ct)
            ? options.Value.QuarantineBucket
            : options.Value.MainBucket;
        var lifetime = TimeSpan.FromMinutes((await platform.GetAsync(ct)).SignedUrlMinutes);
        var url = await storage.PresignGetAsync(bucket, file.ObjectKey, lifetime, "inline", ct);

        var now = clock.UtcNow;
        audit.Add(Log(file, command.ActorId, "moderation.preview", "success", command.Audit, null, now));
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new ContentReviewPreviewView(url, now + lifetime, file.DetectedContentType));
    }

    internal static StorageAccessLog Log(
        FileObject file,
        Guid actorId,
        string action,
        string outcome,
        RequestAuditContext context,
        string? details,
        DateTime nowUtc
    ) =>
        StorageAccessLog.Create(
            file.TenantId,
            file.Id,
            actorId,
            action,
            outcome,
            context.IpAddress,
            context.UserAgent,
            context.CorrelationId,
            details,
            nowUtc
        );
}

/// <summary>
/// El revisor confirma que el contenido es apto: vuelve de cuarentena al bucket principal, cuenta en la
/// cuota y se publica como cualquier archivo limpio. Idempotente ante un reintento tras mover el objeto.
/// </summary>
public sealed record ApproveContentReviewCommand(Guid TenantId, Guid FileId, Guid ActorId, RequestAuditContext Audit);

public static class ApproveContentReviewHandler
{
    public static async Task<Result> Handle(
        ApproveContentReviewCommand command,
        IFileObjectRepository files,
        IStorageLimitRepository limits,
        IObjectStorage storage,
        IStorageAuditRepository audit,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
            return Result.Failure(FileErrors.NotFound);
        if (file.Status != FileStatus.PendingReview)
            return Result.Failure(ContentModerationErrors.NotUnderModeration);

        var limit = await limits.GetAsync(command.TenantId, ct);
        if (limit is null)
            return Result.Failure(QuotaErrors.NotProvisioned);
        var reserved = limit.Reserve(file.SizeBytes);
        if (reserved.IsFailure)
            return reserved;

        var now = clock.UtcNow;
        var approved = file.ApproveReview(now, command.Audit.CorrelationId);
        if (approved.IsFailure)
            return approved;

        // El objeto puede seguir en temp si RelocateScannedBlob todavía no lo movió a cuarentena.
        var main = options.Value.MainBucket;
        var source = await ScanFileHandler.LocateStoredObjectAsync(storage, options.Value, file.ObjectKey, ct);
        if (source is not null && source != main)
            await storage.CopyAsync(source, file.ObjectKey, main, ct);
        foreach (var bucket in new[] { options.Value.TempBucket, options.Value.QuarantineBucket })
        {
            if (await storage.ExistsAsync(bucket, file.ObjectKey, ct))
                await storage.DeleteAsync(bucket, file.ObjectKey, ct);
        }
        if (file.FolderType == FolderType.Avatars)
            await storage.SetObjectTagsAsync(main, file.ObjectKey, StorageObjectTags.Avatar, ct);

        limit.Commit(file.SizeBytes);
        audit.Add(
            PreviewContentReviewFileHandler.Log(
                file,
                command.ActorId,
                "moderation.review",
                "approved",
                command.Audit,
                null,
                now
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>El revisor confirma la violación: queda bloqueado en cuarentena y retenido como evidencia.</summary>
public sealed record RejectContentReviewCommand(
    Guid TenantId,
    Guid FileId,
    Guid ActorId,
    string? Notes,
    RequestAuditContext Audit
);

public static class RejectContentReviewHandler
{
    public const int NotesMaxLength = 2000;
    private const string Report = "Blocked after content review.";

    public static async Task<Result> Handle(
        RejectContentReviewCommand command,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
            return Result.Failure(FileErrors.NotFound);
        if (file.Status != FileStatus.PendingReview)
            return Result.Failure(ContentModerationErrors.NotUnderModeration);

        var now = clock.UtcNow;
        var rejected = file.RejectReview(Report, now, command.Audit.CorrelationId);
        if (rejected.IsFailure)
            return rejected;
        if (!file.IsLegalHeld)
            file.PlaceLegalHold();

        audit.Add(
            PreviewContentReviewFileHandler.Log(
                file,
                command.ActorId,
                "moderation.review",
                "rejected",
                command.Audit,
                Trim(command.Notes),
                now
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    internal static string? Trim(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null
        : notes.Length > NotesMaxLength ? notes[..NotesMaxLength]
        : notes.Trim();
}

/// <summary>
/// "Reportar": deja constancia de que el material se reportó (p. ej. a NCMEC; la denuncia la hace una
/// persona, no el sistema) y lo retiene con legal hold para que nada lo purgue. Idempotente.
/// </summary>
public sealed record ReportContentReviewFileCommand(
    Guid TenantId,
    Guid FileId,
    Guid ActorId,
    string? Notes,
    RequestAuditContext Audit
);

public sealed record ReportContentReviewFileResponse(bool LegalHoldPlaced);

public static class ReportContentReviewFileHandler
{
    public static async Task<Result<ReportContentReviewFileResponse>> Handle(
        ReportContentReviewFileCommand command,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null)
            return Result.Failure<ReportContentReviewFileResponse>(FileErrors.NotFound);
        if (file.Status is not (FileStatus.PendingReview or FileStatus.BlockedByPolicy))
            return Result.Failure<ReportContentReviewFileResponse>(ContentModerationErrors.NotUnderModeration);

        var placed = !file.IsLegalHeld;
        if (placed)
            file.PlaceLegalHold();
        audit.Add(
            PreviewContentReviewFileHandler.Log(
                file,
                command.ActorId,
                "moderation.reported",
                "success",
                command.Audit,
                RejectContentReviewHandler.Trim(command.Notes),
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new ReportContentReviewFileResponse(placed));
    }
}
