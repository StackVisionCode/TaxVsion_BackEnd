using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;

namespace TaxVision.Tasks.Application.Dependencies.Queries;

public sealed record GetTaskDependencyGraphQuery(Guid TenantId, Guid TaskId, Guid ActorUserId, bool CanViewAll);

/// <param name="Edges">Cada arista es «la tarea depende de».</param>
public sealed record TaskDependencyGraphResponse(Guid TaskId, IReadOnlyList<TaskDependencyEdgeResponse> Edges);

public sealed record TaskDependencyEdgeResponse(Guid TaskId, Guid DependsOnTaskId);

/// <summary>
/// Aplana el componente conexo aguas arriba. Sale de una sola consulta recursiva, no de un recorrido
/// nivel por nivel.
/// </summary>
public static class GetTaskDependencyGraphHandler
{
    public static async Task<Result<TaskDependencyGraphResponse>> Handle(
        GetTaskDependencyGraphQuery query,
        ITaskDependencyRepository dependencies,
        ITaskRepository tasks,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var viewer = TaskReadGate.ViewerOf(visibility, query.ActorUserId, query.CanViewAll);
        var visible = await TaskReadGate.EnsureVisibleAsync(tasks, query.TenantId, query.TaskId, viewer, ct);
        if (visible.IsFailure)
            return Result.Failure<TaskDependencyGraphResponse>(visible.Error);

        var graph = await dependencies.LoadUpstreamGraphAsync(query.TenantId, query.TaskId, ct);

        var edges = graph
            .SelectMany(entry => entry.Value.Select(dependsOn => new TaskDependencyEdgeResponse(entry.Key, dependsOn)))
            // La consulta recursiva repite una arista si dos caminos llegan a ella.
            .Distinct()
            .ToList();

        return Result.Success(new TaskDependencyGraphResponse(query.TaskId, edges));
    }
}
