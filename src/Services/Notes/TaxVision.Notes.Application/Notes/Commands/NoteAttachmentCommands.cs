using BuildingBlocks.Common;
using BuildingBlocks.Messaging.NotesIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;
using Wolverine;

namespace TaxVision.Notes.Application.Notes.Commands;

/// <summary>
/// Caso B (ADR-07): el frontend ya subió el archivo a CloudStorage y lo validó (ClamAV corre del
/// lado de CloudStorage, no acá) — este command solo enlaza el <c>fileId</c> ya conocido a la
/// nota, en estado <c>Pending</c> hasta que el consumer de <c>FileAvailableIntegrationEvent</c>
/// (Fase 7) lo mueva a <c>Available</c>. Mismo criterio de autoría que
/// <c>NoteMetadataCommands.cs</c>: adjuntar es mutar el contenido de la nota, solo el autor.
/// </summary>
public sealed record AttachFileToNoteCommand(
    Guid TenantId,
    Guid NoteId,
    Guid ActorUserId,
    Guid CloudStorageFileId,
    string DisplayName,
    string ContentType,
    long SizeBytes
);

public static class AttachFileToNoteHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        AttachFileToNoteCommand command,
        INoteRepository notes,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var note = await notes.GetByIdAsync(command.TenantId, command.NoteId, ct);
        if (note is null)
            return Result.Failure<NoteResponse>(NoteErrors.NotFound);
        if (!NoteVisibilityPolicy.CanEditContent(note, command.ActorUserId))
            return Result.Failure<NoteResponse>(NoteErrors.Forbidden);

        var result = note.AttachFile(
            command.CloudStorageFileId,
            command.DisplayName,
            command.ContentType,
            command.SizeBytes
        );
        if (result.IsFailure)
            return Result.Failure<NoteResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(NoteResponse.From(note));
    }
}

public sealed record DetachFileFromNoteCommand(Guid TenantId, Guid NoteId, Guid ActorUserId, Guid CloudStorageFileId);

public static class DetachFileFromNoteHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        DetachFileFromNoteCommand command,
        INoteRepository notes,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var note = await notes.GetByIdAsync(command.TenantId, command.NoteId, ct);
        if (note is null)
            return Result.Failure<NoteResponse>(NoteErrors.NotFound);
        if (!NoteVisibilityPolicy.CanEditContent(note, command.ActorUserId))
            return Result.Failure<NoteResponse>(NoteErrors.Forbidden);

        var result = note.DetachFile(command.CloudStorageFileId);
        if (result.IsFailure)
            return Result.Failure<NoteResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new NoteAttachmentDetachedIntegrationEvent
            {
                TenantId = note.TenantId,
                CorrelationId = correlation.CorrelationId,
                NoteId = note.Id,
                CloudStorageFileId = command.CloudStorageFileId,
            }
        );

        return Result.Success(NoteResponse.From(note));
    }
}
