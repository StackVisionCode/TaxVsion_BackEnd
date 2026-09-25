using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes.Queries;

// CanViewAllCustomers: visibilidad por asignación (P2), ortogonal a ActorHasViewAll (notes.view_all).
public sealed record GetNoteQuery(
    Guid TenantId,
    Guid NoteId,
    Guid ActorUserId,
    bool ActorHasViewAll,
    bool CanViewAllCustomers = true
);

/// <summary>03_Plan_De_Fases.md §Fase 5 — devuelve <see cref="NoteErrors.NotFound"/> tanto si la nota no existe como si existe pero no es visible para el actor (nunca revela la existencia de una nota que no puede ver). P2: si el target es un Customer no asignado al actor (sin customers.view_all y con el flag), también NotFound.</summary>
public static class GetNoteHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        GetNoteQuery query,
        INoteRepository notes,
        IOptions<NotesVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var note = await notes.GetByIdAsync(query.TenantId, query.NoteId, ct);
        if (note is null || !NoteVisibilityPolicy.CanStaffView(note, query.ActorUserId, query.ActorHasViewAll))
            return Result.Failure<NoteResponse>(NoteErrors.NotFound);

        // Gate por asignación: notas cuyo target es un Customer no asignado al actor no se revelan.
        var restrict = visibility.Value.Enabled && !query.CanViewAllCustomers;
        if (
            restrict
            && note.Reference.TargetType == NoteTargetType.Customer
            && note.Reference.TargetId is { } customerId
            && !await notes.IsCustomerAssignedAsync(query.TenantId, customerId, query.ActorUserId, ct)
        )
        {
            return Result.Failure<NoteResponse>(NoteErrors.NotFound);
        }

        return Result.Success(NoteResponse.From(note));
    }
}
