using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

/// <summary>
/// Lo que cuelga de una tarea (temporizadores, adjuntos, grafo) se ve si se ve la tarea: misma regla que el detalle.
/// </summary>
public static class TaskReadGate
{
    /// <summary>null = sin recorte (visibilidad apagada o quien ve todos los clientes).</summary>
    public static Guid? ViewerOf(IOptions<TasksVisibilityOptions> visibility, Guid actorUserId, bool canViewAll) =>
        visibility.Value.Enabled && !canViewAll ? actorUserId : null;

    public static async Task<Result> EnsureVisibleAsync(
        ITaskRepository tasks,
        Guid tenantId,
        Guid taskId,
        Guid? viewer,
        CancellationToken ct
    )
    {
        if (viewer is null)
            return Result.Success();
        var found = await tasks.GetByIdForReadAsync(tenantId, taskId, viewer, ct);
        return found.IsFailure ? Result.Failure(found.Error) : Result.Success();
    }
}
