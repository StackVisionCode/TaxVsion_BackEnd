using BuildingBlocks.Common;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Queries;

public static class ListFormRequestsHandler
{
    public static Task<PagedResult<FormRequestListItemResponse>> Handle(
        ListFormRequestsQuery query,
        IFormRequestStaffReadRepository readRepository,
        CancellationToken ct
    )
    {
        var normalized = query with
        {
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
        };
        return readRepository.ListAsync(normalized, DateTime.UtcNow, ct);
    }
}
