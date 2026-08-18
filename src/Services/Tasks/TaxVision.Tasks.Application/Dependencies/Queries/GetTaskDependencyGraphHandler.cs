using TaxVision.Tasks.Application.Dependencies.Abstractions;

namespace TaxVision.Tasks.Application.Dependencies.Queries;

public sealed record GetTaskDependencyGraphQuery(Guid TenantId, Guid TaskId);

/// <param name="Edges">Cada arista es «la tarea depende de».</param>
public sealed record TaskDependencyGraphResponse(Guid TaskId, IReadOnlyList<TaskDependencyEdgeResponse> Edges);

public sealed record TaskDependencyEdgeResponse(Guid TaskId, Guid DependsOnTaskId);

/// <summary>
/// Aplana el componente conexo aguas arriba. Sale de una sola consulta recursiva, no de un recorrido
/// nivel por nivel.
/// </summary>
public static class GetTaskDependencyGraphHandler
{
    public static async Task<TaskDependencyGraphResponse> Handle(
        GetTaskDependencyGraphQuery query,
        ITaskDependencyRepository dependencies,
        CancellationToken ct
    )
    {
        var graph = await dependencies.LoadUpstreamGraphAsync(query.TenantId, query.TaskId, ct);

        var edges = graph
            .SelectMany(entry => entry.Value.Select(dependsOn => new TaskDependencyEdgeResponse(entry.Key, dependsOn)))
            .ToList();

        return new TaskDependencyGraphResponse(query.TaskId, edges);
    }
}
