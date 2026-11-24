using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Statuses.Queries;

public sealed record ListWorkflowStatusesQuery(Guid TenantId, bool IncludeArchived);

/// <summary>Los estados de la oficina más los del sistema sin fila. Sustituirá a las taxonomías.</summary>
public static class ListWorkflowStatusesHandler
{
    public static async Task<WorkflowStatusesResponse> Handle(
        ListWorkflowStatusesQuery query,
        IWorkflowStatusRepository statuses,
        CancellationToken ct
    )
    {
        var composed = WorkflowStatusCatalog.Compose(
            await statuses.ListAsync(query.TenantId, query.IncludeArchived, ct)
        );
        var tallies = await statuses.CountOpenTasksByCodeAsync(query.TenantId, ct);
        return new(WorkflowStatusCatalog.WithOpenCounts(composed, tallies));
    }
}
