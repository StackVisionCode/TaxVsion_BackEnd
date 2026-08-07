using BuildingBlocks.Results;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes.Queries;

public sealed record GetNoteQuery(Guid TenantId, Guid NoteId, Guid ActorUserId, bool ActorHasViewAll);

/// <summary>03_Plan_De_Fases.md §Fase 5 — devuelve <see cref="NoteErrors.NotFound"/> tanto si la nota no existe como si existe pero no es visible para el actor (nunca revela la existencia de una nota que no puede ver).</summary>
public static class GetNoteHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        GetNoteQuery query,
        INoteRepository notes,
        CancellationToken ct
    )
    {
        var note = await notes.GetByIdAsync(query.TenantId, query.NoteId, ct);
        if (note is null || !NoteVisibilityPolicy.CanStaffView(note, query.ActorUserId, query.ActorHasViewAll))
            return Result.Failure<NoteResponse>(NoteErrors.NotFound);

        return Result.Success(NoteResponse.From(note));
    }
}
