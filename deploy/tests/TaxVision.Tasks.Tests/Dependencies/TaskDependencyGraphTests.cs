using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Tests.Dependencies;

public sealed class TaskDependencyGraphTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    [Fact]
    public void EnsureNoCycle_allows_an_edge_into_an_empty_graph()
    {
        var result = TaskDependencyGraph.EnsureNoCycle(A, B, Graph());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void EnsureNoCycle_rejects_the_direct_back_edge()
    {
        // B ya depende de A, así que A→B cerraría el ciclo.
        var result = TaskDependencyGraph.EnsureNoCycle(A, B, Graph((B, [A])));

        Assert.True(result.IsFailure);
        Assert.Equal(TaskErrors.Dependency.Cycle, result.Error);
    }

    [Fact]
    public void EnsureNoCycle_rejects_a_cycle_two_hops_upstream()
    {
        // A→B→C y ahora C→A: el ciclo sólo se ve recorriendo hacia arriba.
        var result = TaskDependencyGraph.EnsureNoCycle(C, A, Graph((A, [B]), (B, [C])));

        Assert.True(result.IsFailure);
        Assert.Equal(TaskErrors.Dependency.Cycle, result.Error);
    }

    [Fact]
    public void EnsureNoCycle_allows_a_diamond()
    {
        // D depende de B y C, que dependen de A. Converge sin ciclo.
        var d = Guid.NewGuid();
        var result = TaskDependencyGraph.EnsureNoCycle(d, C, Graph((B, [A]), (C, [A]), (d, [B])));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void EnsureNoCycle_gives_up_on_a_graph_over_the_cap()
    {
        // Cadena lineal más larga que el tope: termina acotado en vez de recorrerla entera.
        var ids = Enumerable.Range(0, TaskDependencyGraph.MaxTraversalNodes + 10).Select(_ => Guid.NewGuid()).ToArray();
        var edges = new Dictionary<Guid, IReadOnlyList<Guid>>();
        for (var i = 0; i < ids.Length - 1; i++)
            edges[ids[i]] = [ids[i + 1]];

        var result = TaskDependencyGraph.EnsureNoCycle(Guid.NewGuid(), ids[0], edges);

        Assert.True(result.IsFailure);
        Assert.Equal(TaskErrors.Dependency.GraphTooLarge, result.Error);
    }

    private static Dictionary<Guid, IReadOnlyList<Guid>> Graph(params (Guid Task, Guid[] DependsOn)[] entries) =>
        entries.ToDictionary(e => e.Task, e => (IReadOnlyList<Guid>)e.DependsOn);
}
