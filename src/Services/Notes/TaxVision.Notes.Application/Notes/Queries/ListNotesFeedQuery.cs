using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes.Queries;

// Central de notas: mismo predicado de visibilidad y asignación que los listados existentes.
public sealed record ListNotesFeedQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool ActorHasViewAll,
    bool CanViewAllCustomers,
    NoteFeedCriteria Criteria,
    int Page,
    int Size
);

public static class ListNotesFeedHandler
{
    public static async Task<Result<PagedResult<NoteFeedItem>>> Handle(
        ListNotesFeedQuery query,
        INoteRepository notes,
        IOptions<NotesVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        // Las privadas de otros solo se piden con notes.view_all.
        if (query.Criteria.Scope == NoteFeedScope.OthersPrivate && !query.ActorHasViewAll)
            return Result.Failure<PagedResult<NoteFeedItem>>(NoteErrors.Forbidden);

        var assignedTo = visibility.Value.Enabled && !query.CanViewAllCustomers ? query.ActorUserId : (Guid?)null;
        var page = await notes.ListFeedAsync(
            query.TenantId,
            query.ActorUserId,
            query.ActorHasViewAll,
            assignedTo,
            query.Criteria,
            query.Page,
            query.Size,
            ct
        );
        var items = page.Items.Select(row => NoteFeedItem.From(row.Note, row.CustomerName)).ToList();
        return Result.Success(new PagedResult<NoteFeedItem>(items, page.Page, page.Size, page.TotalCount));
    }
}
