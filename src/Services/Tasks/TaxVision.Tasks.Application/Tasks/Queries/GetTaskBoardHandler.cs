using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Queries;

// ActorUserId + CanViewAll: visibilidad por asignación (P2) — quien no ve todo solo ve tareas sin cliente,
// las que tiene asignadas/creó, o las de sus clientes asignados.
public sealed record GetTaskBoardQuery(
    Guid TenantId,
    TaskQueryFilter Filter,
    int Take,
    Guid ActorUserId,
    bool CanViewAll
);

/// <summary>
/// Devuelve una columna por cada valor de <see cref="TaskItemStatus"/>, incluidas las vacías: un
/// Kanban al que le faltan columnas según los datos no se puede usar para arrastrar tarjetas.
/// </summary>
public static class GetTaskBoardHandler
{
    public static async Task<TaskBoardResponse> Handle(
        GetTaskBoardQuery query,
        ITaskRepository tasks,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var items = await tasks.ListForBoardAsync(query.TenantId, query.Filter, query.Take, assignedTo, ct);
        var byStatus = items.ToLookup(t => t.Status);

        var columns = Enum.GetValues<TaskItemStatus>()
            .Select(status => new TaskBoardColumn(status, [.. byStatus[status].Select(TaskResponse.From)]))
            .ToList();

        return new TaskBoardResponse(columns, items.Count);
    }
}
