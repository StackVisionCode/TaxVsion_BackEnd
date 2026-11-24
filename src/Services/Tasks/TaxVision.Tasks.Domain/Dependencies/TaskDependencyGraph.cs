using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.Dependencies;

// Iterativo, no recursivo: un StackOverflowException sobre un grafo que arma el usuario no se atrapa.
public static class TaskDependencyGraph
{
    public const int MaxTraversalNodes = 500;

    /// <summary>Topes de la consulta recursiva que carga el grafo: niveles que baja y filas que trae.</summary>
    public const int MaxQueryLevel = 20;
    public const int MaxQueryEdges = 500;

    /// <summary>
    /// La consulta no expande un nodo a más de <see cref="MaxQueryLevel"/> + 1 aristas del inicio, ni trae más
    /// de <see cref="MaxQueryEdges"/> filas. Si llegó a cualquiera de los dos, un ciclo podría quedar fuera.
    /// </summary>
    /// <param name="edges">Sucesora → predecesoras, tal como las cargó la consulta.</param>
    public static bool IsTruncated(Guid start, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> edges)
    {
        if (edges.Values.Sum(predecessors => predecessors.Count) >= MaxQueryEdges)
            return true;

        var distance = new Dictionary<Guid, int> { [start] = 0 };
        var pending = new Queue<Guid>();
        pending.Enqueue(start);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!edges.TryGetValue(current, out var predecessors))
                continue;

            foreach (var predecessor in predecessors)
            {
                if (distance.ContainsKey(predecessor))
                    continue;

                distance[predecessor] = distance[current] + 1;
                if (distance[predecessor] > MaxQueryLevel)
                    return true;
                pending.Enqueue(predecessor);
            }
        }

        return false;
    }

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
