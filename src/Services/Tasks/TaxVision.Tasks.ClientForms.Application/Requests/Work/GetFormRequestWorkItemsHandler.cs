using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Work;

/// <summary>Lo pide <c>/tasks/work/{userId}/items</c> después de autorizar al actor (propio o <c>users.work.view</c>).</summary>
public sealed record GetFormRequestWorkItemsQuery(
    Guid TenantId,
    Guid ActorUserId,
    Guid UserId,
    bool CanViewAll,
    int Page,
    int Size
);

public static class GetFormRequestWorkItemsHandler
{
    public static Task<PagedResult<WorkItem>> Handle(
        GetFormRequestWorkItemsQuery query,
        IFormRequestWorkReader reader,
        CancellationToken ct
    ) =>
        reader.ItemsAsync(
            query.TenantId,
            query.UserId,
            query.ActorUserId,
            query.CanViewAll,
            query.Page,
            query.Size,
            ct
        );
}
