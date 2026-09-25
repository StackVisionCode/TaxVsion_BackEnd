using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record ListTasksByCustomerQuery(
    Guid TenantId,
    Guid CustomerId,
    int? TaxYear,
    int Page,
    int Size,
    Guid ActorUserId,
    bool CanViewAll
);

public static class ListTasksByCustomerHandler
{
    public static async Task<PagedResult<TaskResponse>> Handle(
        ListTasksByCustomerQuery query,
        ITaskRepository tasks,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var page = await tasks.ListByCustomerAsync(
            query.TenantId,
            query.CustomerId,
            query.TaxYear,
            query.Page,
            query.Size,
            assignedTo,
            ct
        );
        return TaskResponse.FromPage(page);
    }
}
