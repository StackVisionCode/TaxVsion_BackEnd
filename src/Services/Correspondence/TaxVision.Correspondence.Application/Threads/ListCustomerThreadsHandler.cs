using BuildingBlocks.Common;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Listado paginado del inbox de un customer (Fase 9) — hilos más reciente primero
/// (<c>LastMessageAtUtc DESC</c>, mismo orden que <see cref="IEmailThreadRepository.FindRecentByCustomerAsync"/>
/// usa internamente, pero acá sin ventana de tiempo ni filtro de estado: el cliente final
/// también quiere ver sus hilos archivados). Filtro puro (tenant + customer), no "carga un
/// recurso" — igual que <c>SearchCustomersHandler</c> de Customer, devuelve <c>PagedResult</c>
/// directo sin envolver en <c>Result</c>: un customer sin hilos no es un error, es una página vacía.
/// </summary>
public static class ListCustomerThreadsHandler
{
    public static async Task<PagedResult<ThreadSummary>> Handle(
        ListCustomerThreadsQuery query,
        IEmailThreadRepository emailThreads,
        CancellationToken ct
    )
    {
        var page = await emailThreads.ListByCustomerAsync(query.TenantId, query.CustomerId, query.Page, query.Size, ct);

        var items = page.Items.Select(ToSummary).ToList();
        return new PagedResult<ThreadSummary>(items, page.Page, page.Size, page.TotalCount);
    }

    private static ThreadSummary ToSummary(EmailThread thread) =>
        new(
            thread.Id,
            thread.Subject,
            thread.Status.ToString(),
            thread.MessageCount,
            thread.FirstMessageAtUtc,
            thread.LastMessageAtUtc
        );
}
