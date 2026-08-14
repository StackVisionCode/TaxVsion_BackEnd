using BuildingBlocks.Common;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record ListSubtasksQuery(Guid TenantId, Guid ParentTaskId, int Page, int Size);

/// <summary>
/// Un nivel por llamada, no el árbol entero: con tope de 50 hijos y 3 niveles el árbol completo son
/// hasta 2.550 filas, y la pantalla muestra un nivel a la vez.
/// </summary>
public static class ListSubtasksHandler
{
    public static async Task<PagedResult<TaskResponse>> Handle(
        ListSubtasksQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var page = await tasks.ListSubtasksAsync(query.TenantId, query.ParentTaskId, query.Page, query.Size, ct);
        return TaskResponse.FromPage(page);
    }
}
