using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Application.Notes.Consumers;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes.Commands;

/// <summary>Aplica a un adjunto en espera el veredicto que CloudStorage ya emitió.</summary>
public sealed record ReconcilePendingAttachmentCommand(Guid TenantId, Guid NoteId, Guid FileId);

/// <summary>
/// El veredicto del escaneo se publica una vez y el consumer lo descarta si el adjunto todavía no
/// existe. Lo programa el attach (30 s) y lo invoca el job de adjuntos en espera. Idempotente: sólo
/// actúa sobre adjuntos Pending/UnderReview. Un error HTTP se lanza para reintentar.
/// </summary>
public static class ReconcilePendingAttachmentHandler
{
    public static async Task Handle(
        ReconcilePendingAttachmentCommand command,
        INoteRepository notes,
        INoteFileScanStatusClient files,
        IUnitOfWork unitOfWork,
        ILogger<ReconcilePendingAttachmentCommand> logger,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var note = await notes.GetByIdAsync(command.TenantId, command.NoteId, ct);
        var attachment = note?.Attachments.FirstOrDefault(a => a.CloudStorageFileId == command.FileId);
        if (note is null || attachment is null)
            return;

        // Quitado o con veredicto final: no se toca.
        if (attachment.Status is not (NoteAttachmentStatus.Pending or NoteAttachmentStatus.UnderReview))
            return;

        var status = await files.GetStatusAsync(command.TenantId, command.FileId, ct);
        var result = Apply(note, attachment, status, clock.GetUtcNow().UtcDateTime);
        if (result is null)
            return;

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Reconcile of file {FileId} on note {NoteId} failed: {ErrorCode}",
                command.FileId,
                note.Id,
                result.Error.Code
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Attachment {FileId} on note {NoteId} reconciled from CloudStorage status {Status}.",
            command.FileId,
            note.Id,
            status
        );
    }

    // null = sin veredicto todavía (o en papelera): la próxima pasada lo vuelve a ver.
    private static Result? Apply(Note note, NoteAttachment attachment, RemoteFileStatus status, DateTime nowUtc) =>
        status switch
        {
            RemoteFileStatus.Available => note.MarkAttachmentAvailable(attachment.CloudStorageFileId, nowUtc),
            RemoteFileStatus.Infected => note.MarkAttachmentRejected(attachment.CloudStorageFileId, "infected", nowUtc),
            RemoteFileStatus.BlockedByPolicy => note.MarkAttachmentRejected(
                attachment.CloudStorageFileId,
                "blocked-by-policy",
                nowUtc
            ),
            RemoteFileStatus.ScanFailed => note.MarkAttachmentRejected(
                attachment.CloudStorageFileId,
                NotesFileScanResultConsumer.ReasonScanFailed,
                nowUtc
            ),
            RemoteFileStatus.NotFound => note.MarkAttachmentRejected(
                attachment.CloudStorageFileId,
                NotesFileScanResultConsumer.ReasonFileMissing,
                nowUtc
            ),
            RemoteFileStatus.PendingReview when attachment.Status is NoteAttachmentStatus.Pending =>
                note.MarkAttachmentUnderReview(attachment.CloudStorageFileId, nowUtc),
            _ => null,
        };
}
