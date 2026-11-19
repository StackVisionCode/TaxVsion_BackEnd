using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Work;

/// <param name="CanViewAll">El actor tiene <c>customers.view_all</c>.</param>
public sealed record GetWorkTitlesQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    IReadOnlyList<Guid> TaskIds,
    IReadOnlyList<Guid> ClientRequestIds
);

/// <summary>
/// Títulos en vivo de tareas y pedidos al cliente con la regla de lectura del detalle. Lo que no existe o quien mira
/// no puede leer se omite.
/// </summary>
public static class GetWorkTitlesHandler
{
    public static async Task<IReadOnlyList<WorkTitle>> Handle(
        GetWorkTitlesQuery query,
        ITaskWorkReader reader,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (query.TaskIds.Count == 0 && query.ClientRequestIds.Count == 0)
            return [];

        var visibleTo = WorkAccess.VisibleTo(query.ActorUserId, query.CanViewAll, visibility.Value.Enabled);
        var titles = await reader.TitlesAsync(query.TenantId, query.TaskIds, query.ClientRequestIds, visibleTo, ct);
        return WorkTitleRefs.Clip(titles);
    }
}
