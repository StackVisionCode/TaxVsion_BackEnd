using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Dependencies.Queries;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Tests.Dependencies;

/// <summary>El grafo no repite aristas aunque la consulta recursiva llegue dos veces a la misma tarea (V-04).</summary>
public sealed class GetTaskDependencyGraphHandlerTests
{
    [Fact]
    public async Task An_edge_reached_twice_is_listed_once()
    {
        var tenant = Guid.NewGuid();
        var (task, blocker) = (Guid.NewGuid(), Guid.NewGuid());
        var dependencies = new InMemoryTaskDependencyRepository();
        dependencies.Seed(tenant, task, blocker);
        dependencies.Seed(tenant, task, blocker);

        var graph = await GetTaskDependencyGraphHandler.Handle(
            new GetTaskDependencyGraphQuery(tenant, task, Guid.NewGuid(), CanViewAll: true),
            dependencies,
            new InMemoryTaskRepository(),
            Options.Create(new TasksVisibilityOptions()),
            CancellationToken.None
        );

        Assert.Single(graph.Value.Edges);
    }
}
