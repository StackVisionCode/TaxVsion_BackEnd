using BuildingBlocks.Common;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record ListTasksByCustomerQuery(Guid TenantId, Guid CustomerId, int? TaxYear, int Page, int Size);

public static class ListTasksByCustomerHandler
{
    public static async Task<PagedResult<TaskResponse>> Handle(
        ListTasksByCustomerQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var page = await tasks.ListByCustomerAsync(
            query.TenantId,
            query.CustomerId,
            query.TaxYear,
            query.Page,
            query.Size,
            ct
        );
        return TaskResponse.FromPage(page);
    }
}
