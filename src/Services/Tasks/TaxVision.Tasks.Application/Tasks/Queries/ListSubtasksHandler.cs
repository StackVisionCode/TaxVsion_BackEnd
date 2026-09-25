using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record ListSubtasksQuery(
    Guid TenantId,
    Guid ParentTaskId,
    int Page,
    int Size,
    Guid ActorUserId,
    bool CanViewAll
);

/// <summary>
/// Un nivel por llamada, no el árbol entero: con tope de 50 hijos y 3 niveles el árbol completo son
/// hasta 2.550 filas, y la pantalla muestra un nivel a la vez.
/// </summary>
public static class ListSubtasksHandler
{
    public static async Task<PagedResult<TaskResponse>> Handle(
        ListSubtasksQuery query,
        ITaskRepository tasks,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var page = await tasks.ListSubtasksAsync(
            query.TenantId,
            query.ParentTaskId,
            query.Page,
            query.Size,
            assignedTo,
            ct
        );
        return TaskResponse.FromPage(page);
    }
}
