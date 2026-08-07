using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Notes.Application.Notes.Commands;

// ---------------------------------------------------------------------------
// 03_Plan_De_Fases.md §Fase 5 — "ChangeNoteVisibilityCommand, PinNoteCommand/UnpinNoteCommand,
// SetNoteColorCommand → autor": mismo chequeo que UpdateNoteContentCommand
// (NoteVisibilityPolicy.CanEditContent), NUNCA el governance override de notes.view_all — un admin
// que solo tiene view_all puede ARCHIVAR/BORRAR ajenas (ver NoteLifecycleCommands.cs) pero no
// tocar metadata/contenido de una nota que no es suya.
// ---------------------------------------------------------------------------

public sealed record ChangeNoteVisibilityCommand(
    Guid TenantId,
    Guid NoteId,
    Guid ActorUserId,
    NoteVisibility NewVisibility
);

public static class ChangeNoteVisibilityHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        ChangeNoteVisibilityCommand command,
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

        var result = note.ChangeVisibility(command.NewVisibility, command.ActorUserId);
        if (result.IsFailure)
            return Result.Failure<NoteResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await UpdateNoteContentHandler.PublishUpdatedAsync(note, correlation, bus);
        return Result.Success(NoteResponse.From(note));
    }
}

public sealed record PinNoteCommand(Guid TenantId, Guid NoteId, Guid ActorUserId);

public static class PinNoteHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        PinNoteCommand command,
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

        var result = note.Pin(command.ActorUserId);
        if (result.IsFailure)
            return Result.Failure<NoteResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await UpdateNoteContentHandler.PublishUpdatedAsync(note, correlation, bus);
        return Result.Success(NoteResponse.From(note));
    }
}

public sealed record UnpinNoteCommand(Guid TenantId, Guid NoteId, Guid ActorUserId);

public static class UnpinNoteHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        UnpinNoteCommand command,
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

        var result = note.Unpin(command.ActorUserId);
        if (result.IsFailure)
            return Result.Failure<NoteResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await UpdateNoteContentHandler.PublishUpdatedAsync(note, correlation, bus);
        return Result.Success(NoteResponse.From(note));
    }
}

public sealed record SetNoteColorCommand(Guid TenantId, Guid NoteId, Guid ActorUserId, NoteColorKind? ColorKind);

public static class SetNoteColorHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        SetNoteColorCommand command,
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

        NoteColor? color = null;
        if (command.ColorKind is { } kind)
        {
            var colorResult = NoteColor.Create(kind);
            if (colorResult.IsFailure)
                return Result.Failure<NoteResponse>(colorResult.Error);
            color = colorResult.Value;
        }

        var result = note.SetColor(color, command.ActorUserId);
        if (result.IsFailure)
            return Result.Failure<NoteResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await UpdateNoteContentHandler.PublishUpdatedAsync(note, correlation, bus);
        return Result.Success(NoteResponse.From(note));
    }
}
