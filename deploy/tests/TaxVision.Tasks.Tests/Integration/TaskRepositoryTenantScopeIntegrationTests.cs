using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// TSK-8, en una base temporal: el repositorio se acota con el tenant que recibe, no con el del contexto. Un job o
/// un scope de Wolverine no traen tenant ambiental, y el filtro global fail-closed les devolvería 0 filas.
/// </summary>
public sealed class TaskRepositoryTenantScopeIntegrationTests : IAsyncLifetime
{
    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private Guid _taskId;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        var task = TaskItem
            .Create(
                _tenantId,
                Guid.NewGuid(),
                TaskTitle.Create("Prepare return").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
        _taskId = task.Id;
        await using var db = _database.NewContext(_tenantId);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task WithoutAmbientTenant_TheTenantArgumentStillFindsTheTask()
    {
        await using var db = NoAmbientTenant();
        var repository = new TaskRepository(db);

        var found = await repository.GetByIdAsync(_tenantId, _taskId);
        var listed = await repository.ListByIdsAsync(_tenantId, [_taskId]);

        Assert.True(found.IsSuccess);
        Assert.Single(listed);
    }

    [Fact]
    public async Task OtherTenantArgument_DoesNotFindTheTask()
    {
        await using var db = _database.NewContext(_tenantId);
        var repository = new TaskRepository(db);

        var found = await repository.GetByIdAsync(Guid.NewGuid(), _taskId);
        var listed = await repository.ListByIdsAsync(Guid.NewGuid(), [_taskId]);

        Assert.True(found.IsFailure);
        Assert.Empty(listed);
    }

    private TasksDbContext NoAmbientTenant() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(_database.ConnectionString).Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
