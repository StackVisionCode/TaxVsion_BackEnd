using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes;

/// <summary>Datos del actor que traen los comandos de escritura sobre una nota existente.</summary>
public interface INoteActorRequest
{
    Guid TenantId { get; }
    Guid NoteId { get; }
    Guid ActorUserId { get; }
    bool ActorHasViewAll { get; }
    bool CanViewAllCustomers { get; }
}

/// <summary>
/// Carga una nota solo si el actor puede VERLA: visibilidad por-nota (<see cref="NoteVisibilityPolicy.CanStaffView"/>)
/// más la asignación de clientes (P2) cuando aplica. Devuelve null en ambos casos para que lectura y escritura
/// respondan 404 sin revelar que la nota existe (N3).
/// </summary>
public sealed class VisibleNoteReader(INoteRepository notes, IOptions<NotesVisibilityOptions> visibility)
{
    public Task<Note?> FindAsync(INoteActorRequest request, CancellationToken ct) =>
        FindAsync(
            request.TenantId,
            request.NoteId,
            request.ActorUserId,
            request.ActorHasViewAll,
            request.CanViewAllCustomers,
            ct
        );

    public Task<Note?> FindAsync(
        Guid tenantId,
        Guid noteId,
        Guid actorUserId,
        bool actorHasViewAll,
        bool canViewAllCustomers,
        CancellationToken ct
    ) =>
        FindAsync(
            notes,
            visibility.Value.Enabled && !canViewAllCustomers,
            tenantId,
            noteId,
            actorUserId,
            actorHasViewAll,
            ct
        );

    public static async Task<Note?> FindAsync(
        INoteRepository notes,
        bool restrictToAssignments,
        Guid tenantId,
        Guid noteId,
        Guid actorUserId,
        bool actorHasViewAll,
        CancellationToken ct
    )
    {
        var note = await notes.GetByIdAsync(tenantId, noteId, ct);
        if (note is null || !NoteVisibilityPolicy.CanStaffView(note, actorUserId, actorHasViewAll))
            return null;

        // Nota de un cliente no asignado al actor: no se revela.
        if (
            restrictToAssignments
            && note.Reference.TargetType == NoteTargetType.Customer
            && note.Reference.TargetId is { } customerId
            && !await notes.IsCustomerAssignedAsync(tenantId, customerId, actorUserId, ct)
        )
            return null;

        return note;
    }
}
