using BuildingBlocks.Results;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes.Queries;

/// <summary>Misma forma que la respuesta de CloudStorage, para que los front no cambien de modelo.</summary>
public sealed record NoteAttachmentDownloadUrlResponse(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);

/// <summary>Personal: la nota con la regla de lectura de <see cref="GetNoteQuery"/>.</summary>
public sealed record GetNoteAttachmentDownloadUrlQuery(
    Guid TenantId,
    Guid NoteId,
    Guid FileId,
    Guid ActorUserId,
    bool ActorHasViewAll,
    bool CanViewAllCustomers,
    bool ForPreview
) : INoteActorRequest;

/// <summary>Portal: solo notas ClientVisible del cliente del token.</summary>
public sealed record GetPortalNoteAttachmentDownloadUrlQuery(
    Guid TenantId,
    Guid CustomerId,
    Guid NoteId,
    Guid FileId,
    bool ForPreview
);

public static class GetNoteAttachmentDownloadUrlHandler
{
    public static async Task<Result<NoteAttachmentDownloadUrlResponse>> Handle(
        GetNoteAttachmentDownloadUrlQuery query,
        VisibleNoteReader notes,
        INoteFileDownloadClient files,
        CancellationToken ct
    )
    {
        var note = await notes.FindAsync(query, ct);
        return await NoteAttachmentDownload.IssueAsync(note, query.FileId, query.ForPreview, files, ct);
    }
}

public static class GetPortalNoteAttachmentDownloadUrlHandler
{
    public static async Task<Result<NoteAttachmentDownloadUrlResponse>> Handle(
        GetPortalNoteAttachmentDownloadUrlQuery query,
        INoteRepository notes,
        INoteFileDownloadClient files,
        CancellationToken ct
    )
    {
        var note = await notes.GetByIdAsync(query.TenantId, query.NoteId, ct);
        var visible =
            note is not null
            && note.Status != NoteStatus.Deleted
            && note.Visibility == NoteVisibility.ClientVisible
            && note.Reference.TargetType == NoteTargetType.Customer
            && note.Reference.TargetId == query.CustomerId;
        return await NoteAttachmentDownload.IssueAsync(
            visible ? note : null,
            query.FileId,
            query.ForPreview,
            files,
            ct
        );
    }
}

internal static class NoteAttachmentDownload
{
    // El archivo tiene que ser un adjunto disponible de esa nota; si no, no encontrada (no se revela nada).
    public static async Task<Result<NoteAttachmentDownloadUrlResponse>> IssueAsync(
        Note? note,
        Guid fileId,
        bool forPreview,
        INoteFileDownloadClient files,
        CancellationToken ct
    )
    {
        var available =
            note?.Attachments.Any(a => a.CloudStorageFileId == fileId && a.Status == NoteAttachmentStatus.Available)
            ?? false;
        if (!available)
            return Result.Failure<NoteAttachmentDownloadUrlResponse>(NoteErrors.NotFound);

        var url = await files.GetDownloadUrlAsync(note!.TenantId, fileId, forPreview, ct);
        return url.IsFailure
            ? Result.Failure<NoteAttachmentDownloadUrlResponse>(url.Error)
            : Result.Success(
                new NoteAttachmentDownloadUrlResponse(fileId, url.Value.DownloadUrl, url.Value.ExpiresAtUtc)
            );
    }
}
