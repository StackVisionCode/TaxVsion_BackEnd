using BuildingBlocks.Common;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Fase 15 — drafts <see cref="DraftStatus.Draft"/> (abiertos/resumibles) de un customer, más
/// reciente primero. HTTP-triggered, no un consumer Wolverine, mismo criterio que
/// <see cref="GetDraftHandler"/> (no empuja correlación). Filtro puro (tenant + customer), no
/// "carga un recurso" — igual que <see cref="Threads.ListCustomerThreadsHandler"/>, devuelve
/// <c>PagedResult</c> directo sin envolver en <c>Result</c>: un customer sin drafts abiertos no es
/// un error, es una página vacía.
/// </summary>
public static class ListDraftsHandler
{
    public static async Task<PagedResult<DraftListItem>> Handle(
        ListDraftsQuery query,
        IDraftRepository drafts,
        CancellationToken ct
    )
    {
        var page = await drafts.ListOpenByCustomerAsync(query.TenantId, query.CustomerId, query.Page, query.Size, ct);

        var items = page.Items.Select(ToListItem).ToList();
        return new PagedResult<DraftListItem>(items, page.Page, page.Size, page.TotalCount);
    }

    private static DraftListItem ToListItem(Draft draft) =>
        new(
            draft.Id,
            draft.Subject,
            draft.Status.ToString(),
            draft.ReplyContext is not null,
            draft.UpdatedAtUtc,
            draft.LastAutoSavedAtUtc
        );
}
