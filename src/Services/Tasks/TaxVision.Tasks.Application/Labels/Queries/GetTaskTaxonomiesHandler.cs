using TaxVision.Tasks.Application.Labels.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Labels.Queries;

public sealed record GetTaskTaxonomiesQuery(Guid TenantId);

/// <param name="Statuses">Los valores del enum, que es lo que el motor lee de verdad.</param>
/// <param name="Priorities">Idem.</param>
/// <param name="Labels">Los nombres que la firma eligió, con el estado al que corresponde cada uno.</param>
public sealed record TaskTaxonomiesResponse(
    IReadOnlyList<string> Statuses,
    IReadOnlyList<string> Priorities,
    IReadOnlyList<TaskLabelResponse> Labels
);

/// <summary>
/// Devuelve los enums y el catálogo juntos para que el front no tenga que hardcodear ninguno de los
/// dos. Los labels son presentación: el estado que manda es el enum.
/// </summary>
public static class GetTaskTaxonomiesHandler
{
    public static async Task<TaskTaxonomiesResponse> Handle(
        GetTaskTaxonomiesQuery query,
        ITaskLabelRepository labels,
        CancellationToken ct
    )
    {
        var tenantLabels = await labels.ListAsync(query.TenantId, ct);

        return new TaskTaxonomiesResponse(
            [.. Enum.GetNames<TaskItemStatus>()],
            [.. Enum.GetNames<TaskPriority>()],
            [.. tenantLabels.Select(TaskLabelResponse.From)]
        );
    }
}
