using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record GetTaskBoardQuery(Guid TenantId, TaskQueryFilter Filter, int Take);

/// <summary>
/// Devuelve una columna por cada valor de <see cref="TaskItemStatus"/>, incluidas las vacías: un
/// Kanban al que le faltan columnas según los datos no se puede usar para arrastrar tarjetas.
/// </summary>
public static class GetTaskBoardHandler
{
    public static async Task<TaskBoardResponse> Handle(
        GetTaskBoardQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var items = await tasks.ListForBoardAsync(query.TenantId, query.Filter, query.Take, ct);
        var byStatus = items.ToLookup(t => t.Status);

        var columns = Enum.GetValues<TaskItemStatus>()
            .Select(status => new TaskBoardColumn(status, [.. byStatus[status].Select(TaskResponse.From)]))
            .ToList();

        return new TaskBoardResponse(columns, items.Count);
    }
}
