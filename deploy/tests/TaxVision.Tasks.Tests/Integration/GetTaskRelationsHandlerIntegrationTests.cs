using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Dependencies.Queries;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>La consulta de relaciones contra SQL real, con la visibilidad por asignación encendida.</summary>
public sealed class GetTaskRelationsHandlerIntegrationTests : IAsyncLifetime
{
    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Returns_waiting_on_and_blocking_from_sql()
    {
        var review = NewTask("Review");
        var file = NewTask("File");
        var notify = NewTask("Notify client");
        await using (var seed = _database.NewContext(_tenantId))
        {
            seed.Tasks.AddRange(review, file, notify);
            seed.TaskDependencies.Add(
                TaskDependency.Create(_tenantId, file.Id, review.Id, _owner, DateTime.UtcNow).Value
            );
            seed.TaskDependencies.Add(
                TaskDependency.Create(_tenantId, notify.Id, file.Id, _owner, DateTime.UtcNow).Value
            );
            await seed.SaveChangesAsync();
        }

        await using var db = _database.NewContext(_tenantId);
        var result = await GetTaskRelationsHandler.Handle(
            new GetTaskRelationsQuery(_tenantId, file.Id, _owner, CanViewAll: false),
            new TaskRepository(db),
            new TaskDependencyRepository(db),
            Options.Create(new TasksVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );

        Assert.Equal("Review", Assert.Single(result.Value.WaitingOn).Title);
        Assert.Equal("Notify client", Assert.Single(result.Value.Blocking).Title);
    }

    private TaskItem NewTask(string title) =>
        TaskItem
            .Create(
                _tenantId,
                _owner,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                _owner,
                DateTime.UtcNow
            )
            .Value;
}
