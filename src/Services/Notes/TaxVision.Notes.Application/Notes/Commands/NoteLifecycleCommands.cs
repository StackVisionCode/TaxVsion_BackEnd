using BuildingBlocks.Common;
using BuildingBlocks.Messaging.NotesIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notes.Domain.Notes;
using Wolverine;

namespace TaxVision.Notes.Application.Notes.Commands;

// ---------------------------------------------------------------------------
// 03_Plan_De_Fases.md §Fase 5 — "ArchiveNoteCommand/RestoreNoteCommand/DeleteNoteCommand → autor O
// notes.view_all (governance: admin puede archivar/borrar ajenas, no editar contenido — ADR-06)".
// Distinto del grupo de NoteMetadataCommands.cs, que es SOLO autor.
// ---------------------------------------------------------------------------

public sealed record ArchiveNoteCommand(
    Guid TenantId,
    Guid NoteId,
    Guid ActorUserId,
    bool ActorHasViewAll,
    bool CanViewAllCustomers = true
) : INoteActorRequest;

public static class ArchiveNoteHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        ArchiveNoteCommand command,
        VisibleNoteReader notes,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var note = await notes.FindAsync(command, ct);
        if (note is null)
            return Result.Failure<NoteResponse>(NoteErrors.NotFound);
        if (!NoteVisibilityPolicy.CanManage(note, command.ActorUserId, command.ActorHasViewAll))
            return Result.Failure<NoteResponse>(NoteErrors.Forbidden);

        var result = note.Archive(command.ActorUserId, clock.GetUtcNow().UtcDateTime);
        if (result.IsFailure)
            return Result.Failure<NoteResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(NoteResponse.From(note));
    }
}

public sealed record RestoreNoteCommand(
    Guid TenantId,
    Guid NoteId,
    Guid ActorUserId,
    bool ActorHasViewAll,
    bool CanViewAllCustomers = true
) : INoteActorRequest;

public static class RestoreNoteHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        RestoreNoteCommand command,
        VisibleNoteReader notes,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var note = await notes.FindAsync(command, ct);
        if (note is null)
            return Result.Failure<NoteResponse>(NoteErrors.NotFound);
        if (!NoteVisibilityPolicy.CanManage(note, command.ActorUserId, command.ActorHasViewAll))
            return Result.Failure<NoteResponse>(NoteErrors.Forbidden);

        var result = note.Restore(command.ActorUserId, clock.GetUtcNow().UtcDateTime);
        if (result.IsFailure)
            return Result.Failure<NoteResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(NoteResponse.From(note));
    }
}

public sealed record DeleteNoteCommand(
    Guid TenantId,
    Guid NoteId,
    Guid ActorUserId,
    bool ActorHasViewAll,
    bool CanViewAllCustomers = true
) : INoteActorRequest;

public static class DeleteNoteHandler
{
    public static async Task<Result> Handle(
        DeleteNoteCommand command,
        VisibleNoteReader notes,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var note = await notes.FindAsync(command, ct);
        if (note is null)
            return Result.Failure(NoteErrors.NotFound);
        if (!NoteVisibilityPolicy.CanManage(note, command.ActorUserId, command.ActorHasViewAll))
            return Result.Failure(NoteErrors.Forbidden);

        var result = note.SoftDelete(command.ActorUserId, clock.GetUtcNow().UtcDateTime);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new NoteDeletedIntegrationEvent
            {
                TenantId = note.TenantId,
                CorrelationId = correlation.CorrelationId,
                NoteId = note.Id,
                ActorUserId = command.ActorUserId,
            }
        );
        return Result.Success();
    }
}
