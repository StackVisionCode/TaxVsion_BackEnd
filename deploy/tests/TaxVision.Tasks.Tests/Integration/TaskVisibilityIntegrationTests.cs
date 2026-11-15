using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// La visibilidad por asignación traducida por SQL Server, en una base temporal: bandeja común, lo propio, lo de sus
/// clientes, y nada de la tarea sin cliente asignada a un colega (tablero, búsqueda y detalle).
/// </summary>
public sealed class TaskVisibilityIntegrationTests : IAsyncLifetime
{
    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();
    private readonly Guid _colleague = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _employeeClient = Guid.NewGuid();
    private readonly Guid _colleagueClient = Guid.NewGuid();
    private Guid _colleaguePrivateId;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        var colleaguePrivate = NewTask("Colleague private", _admin, null, _colleague);
        _colleaguePrivateId = colleaguePrivate.Id;
        await using var db = _database.NewContext(_tenantId);
        db.Tasks.AddRange(
            NewTask("Office pool", _admin, null, null),
            colleaguePrivate,
            NewTask("Mine", _admin, null, _employee),
            NewTask("Created by me for a colleague", _employee, null, _colleague),
            NewTask("My client task", _admin, _employeeClient, null),
            NewTask("Colleague client task", _admin, _colleagueClient, _colleague)
        );
        db.Set<CustomerAssignmentProjection>()
            .AddRange(
                CustomerAssignmentProjection.Create(_tenantId, _employeeClient, _employee, DateTime.UtcNow),
                CustomerAssignmentProjection.Create(_tenantId, _colleagueClient, _colleague, DateTime.UtcNow)
            );
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task The_employee_sees_only_the_pool_his_own_work_and_his_clients()
    {
        await using var db = NoAmbientTenant();
        var repository = new TaskRepository(db);
        string[] expected = ["Created by me for a colleague", "Mine", "My client task", "Office pool"];

        var board = await repository.ListForBoardAsync(_tenantId, new TaskQueryFilter(), 50, _employee);
        var search = await repository.SearchAsync(_tenantId, new TaskQueryFilter(), 1, 50, _employee);
        var detail = await repository.GetByIdForReadAsync(_tenantId, _colleaguePrivateId, _employee);

        Assert.Equal(expected, board.Select(t => t.Title.Value).Order());
        Assert.Equal(expected, search.Items.Select(t => t.Title.Value).Order());
        Assert.True(detail.IsFailure);
    }

    [Fact]
    public async Task The_assignee_and_an_unrestricted_reader_still_see_the_private_task()
    {
        await using var db = NoAmbientTenant();
        var repository = new TaskRepository(db);

        Assert.True((await repository.GetByIdForReadAsync(_tenantId, _colleaguePrivateId, _colleague)).IsSuccess);
        Assert.Equal(6, (await repository.ListForBoardAsync(_tenantId, new TaskQueryFilter(), 50, null)).Count);
    }

    [Fact]
    public async Task A_subtask_follows_its_parent_down_to_the_grandchild()
    {
        var colleaguePrivate = NewTask("Colleague private root", _admin, null, _colleague);
        var hiddenChild = Subtask(colleaguePrivate, "Under colleague: unassigned", null);
        var mine = NewTask("Mine root", _admin, null, _employee);
        var myChild = Subtask(mine, "Under mine: unassigned", null);
        var clientRoot = NewTask("Client root", _admin, _employeeClient, _colleague);
        await using (var seed = _database.NewContext(_tenantId))
        {
            seed.Tasks.AddRange(
                colleaguePrivate,
                hiddenChild,
                Subtask(hiddenChild, "Under colleague: grandchild", null),
                mine,
                myChild,
                Subtask(myChild, "Under mine: grandchild", null),
                Subtask(mine, "Under mine: assigned to colleague", _colleague),
                clientRoot,
                Subtask(clientRoot, "Under my client: assigned to colleague", _colleague)
            );
            await seed.SaveChangesAsync();
        }
        await using var db = NoAmbientTenant();
        var repository = new TaskRepository(db);

        var visible = (await repository.ListForBoardAsync(_tenantId, new TaskQueryFilter(), 100, _employee))
            .Select(t => t.Title.Value)
            .ToHashSet();
        var children = await repository.ListSubtasksAsync(_tenantId, colleaguePrivate.Id, 1, 50, _employee);

        Assert.DoesNotContain("Colleague private root", visible);
        Assert.DoesNotContain("Under colleague: unassigned", visible);
        Assert.DoesNotContain("Under colleague: grandchild", visible);
        Assert.Contains("Under mine: unassigned", visible);
        Assert.Contains("Under mine: grandchild", visible);
        Assert.Contains("Under mine: assigned to colleague", visible);
        Assert.Contains("Under my client: assigned to colleague", visible);
        Assert.Empty(children.Items);
    }

    private static TaskItem Subtask(TaskItem parent, string title, Guid? assignee) =>
        TaskItem
            .CreateSubtask(
                parent,
                parent.CreatedByUserId,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                assignee,
                DateTime.UtcNow
            )
            .Value;

    private TaskItem NewTask(string title, Guid createdBy, Guid? customerId, Guid? assignee) =>
        TaskItem
            .Create(
                _tenantId,
                createdBy,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                customerId is null ? TaskReference.None : TaskReference.Create(customerId, null).Value,
                null,
                null,
                assignee,
                DateTime.UtcNow
            )
            .Value;

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
