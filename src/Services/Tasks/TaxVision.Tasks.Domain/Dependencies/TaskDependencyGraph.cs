using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.Dependencies;

// Iterativo, no recursivo: un StackOverflowException sobre un grafo que arma el usuario no se atrapa.
public static class TaskDependencyGraph
{
    public const int MaxTraversalNodes = 500;

    /// <param name="edges">Sucesora → predecesoras.</param>
    public static Result EnsureNoCycle(Guid from, Guid dependsOn, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> edges)
    {
        if (from == dependsOn)
            return Result.Failure(TaskErrors.Dependency.SelfReference);

        var visited = new HashSet<Guid> { dependsOn };
        var pending = new Queue<Guid>();
        pending.Enqueue(dependsOn);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!edges.TryGetValue(current, out var predecessors))
                continue;

            foreach (var predecessor in predecessors)
            {
                if (predecessor == from)
                    return Result.Failure(TaskErrors.Dependency.Cycle);

                if (!visited.Add(predecessor))
                    continue;

                if (visited.Count > MaxTraversalNodes)
                    return Result.Failure(TaskErrors.Dependency.GraphTooLarge);

                pending.Enqueue(predecessor);
            }
        }

        return Result.Success();
    }
}
