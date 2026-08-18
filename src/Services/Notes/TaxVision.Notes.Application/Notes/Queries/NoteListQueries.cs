using BuildingBlocks.Common;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes.Queries;

// ---------------------------------------------------------------------------
// 03_Plan_De_Fases.md §Fase 5 — el filtro de visibilidad se empuja a SQL en
// NoteRepository.ListByReferenceAsync/SearchAsync (NoteVisibilityPolicy.CanStaffView traducida
// como expresión EF), nunca se filtra en memoria post-página (rompería TotalCount/tamaño de
// página). ListClientVisibleQuery usa un método de repo distinto y más estricto: no recibe actor,
// solo Status!=Deleted && Visibility==ClientVisible — el portal NUNCA ve Team/Private.
// ---------------------------------------------------------------------------

public sealed record ListNotesByReferenceQuery(
    Guid TenantId,
    NoteTargetType TargetType,
    Guid TargetId,
    Guid ActorUserId,
    bool ActorHasViewAll,
    int Page,
    int Size
);

public static class ListNotesByReferenceHandler
{
    public static async Task<PagedResult<NoteResponse>> Handle(
        ListNotesByReferenceQuery query,
        INoteRepository notes,
        CancellationToken ct
    )
    {
        var result = await notes.ListByReferenceAsync(
            query.TenantId,
            query.TargetType,
            query.TargetId,
            query.ActorUserId,
            query.ActorHasViewAll,
            query.Page,
            query.Size,
            ct
        );
        return ToResponse(result);
    }

    internal static PagedResult<NoteResponse> ToResponse(PagedResult<Note> result) =>
        new(result.Items.Select(NoteResponse.From).ToList(), result.Page, result.Size, result.TotalCount);
}

public sealed record ListMyNotesQuery(Guid TenantId, Guid AuthorUserId, int Page, int Size);

/// <summary>"Mis notas": sin filtro de visibilidad extra, el autor siempre ve las suyas.</summary>
public static class ListMyNotesHandler
{
    public static async Task<PagedResult<NoteResponse>> Handle(
        ListMyNotesQuery query,
        INoteRepository notes,
        CancellationToken ct
    )
    {
        var result = await notes.ListForAuthorAsync(query.TenantId, query.AuthorUserId, query.Page, query.Size, ct);
        return ListNotesByReferenceHandler.ToResponse(result);
    }
}

public sealed record SearchNotesQuery(
    Guid TenantId,
    string Term,
    Guid ActorUserId,
    bool ActorHasViewAll,
    int Page,
    int Size
);

/// <summary>Categoría H (03_Plan_De_Fases.md) — búsqueda simple sobre <c>ContentPreview</c>, sin full-text en v1.</summary>
public static class SearchNotesHandler
{
    public static async Task<PagedResult<NoteResponse>> Handle(
        SearchNotesQuery query,
        INoteRepository notes,
        CancellationToken ct
    )
    {
        var result = await notes.SearchAsync(
            query.TenantId,
            query.Term,
            query.ActorUserId,
            query.ActorHasViewAll,
            query.Page,
            query.Size,
            ct
        );
        return ListNotesByReferenceHandler.ToResponse(result);
    }
}

public sealed record ListClientVisibleNotesQuery(
    Guid TenantId,
    NoteTargetType TargetType,
    Guid TargetId,
    int Page,
    int Size
);

/// <summary>CustomerPortal (<c>notes.portal.read</c>) — solo notas <see cref="NoteVisibility.ClientVisible"/> de un target concreto (típicamente su propio Customer).</summary>
public static class ListClientVisibleNotesHandler
{
    public static async Task<PagedResult<NoteResponse>> Handle(
        ListClientVisibleNotesQuery query,
        INoteRepository notes,
        CancellationToken ct
    )
    {
        var result = await notes.ListClientVisibleAsync(
            query.TenantId,
            query.TargetType,
            query.TargetId,
            query.Page,
            query.Size,
            ct
        );
        return ListNotesByReferenceHandler.ToResponse(result);
    }
}
