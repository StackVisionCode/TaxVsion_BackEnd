using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// Qué tareas ve un empleado con la visibilidad por asignación: las suyas, las que creó, las de sus clientes y la
/// bandeja común (sin cliente y sin nadie asignado). Una tarea sin cliente asignada a otro es de ese otro.
/// </summary>
public sealed class TaskVisibilityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Employee = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid EmployeeClient = Guid.NewGuid();
    private static readonly Guid ColleagueClient = Guid.NewGuid();

    [Fact]
    public async Task An_employee_sees_the_office_pool_and_his_own_work_but_not_a_colleague_private_task()
    {
        await using var db = await SeedAsync();

        var titles = (
            (await new TaskRepository(db).SearchAsync(TenantId, new TaskQueryFilter(), 1, 50, Employee)).Items
        )
            .Select(t => t.Title.Value)
            .Order()
            .ToList();

        Assert.Equal(["Created by me for a colleague", "Mine", "My client task", "Office pool"], titles);
    }

    [Fact]
    public async Task A_colleague_task_without_a_client_is_not_found_by_id_for_the_employee()
    {
        await using var db = await SeedAsync();
        var hidden = db
            .ChangeTracker.Entries<TaskItem>()
            .Single(e => e.Entity.Title.Value == "Colleague private")
            .Entity;

        var asEmployee = await new TaskRepository(db).GetByIdForReadAsync(TenantId, hidden.Id, Employee);
        var asColleague = await new TaskRepository(db).GetByIdForReadAsync(TenantId, hidden.Id, Colleague);

        Assert.True(asEmployee.IsFailure);
        Assert.True(asColleague.IsSuccess);
    }

    [Fact]
    public async Task Without_restriction_every_task_is_listed()
    {
        await using var db = await SeedAsync();

        var all = (await new TaskRepository(db).SearchAsync(TenantId, new TaskQueryFilter(), 1, 50, null)).Items;

        Assert.Equal(6, all.Count);
    }

    [Fact]
    public async Task A_subtask_follows_its_parent_unless_it_is_the_employee_own_work()
    {
        await using var db = NewContext();
        var colleaguePrivate = NewTask("Colleague private", Admin, null, Colleague);
        var hiddenChild = Subtask(colleaguePrivate, "Under colleague: unassigned", null);
        var mine = NewTask("Mine", Admin, null, Employee);
        var myChild = Subtask(mine, "Under mine: unassigned", null);
        var pool = NewTask("Office pool", Admin, null, null);
        db.Tasks.AddRange(
            colleaguePrivate,
            hiddenChild,
            Subtask(hiddenChild, "Under colleague: grandchild", null),
            Subtask(colleaguePrivate, "Under colleague: assigned to me", Employee),
            mine,
            myChild,
            Subtask(myChild, "Under mine: grandchild", null),
            Subtask(mine, "Under mine: assigned to colleague", Colleague),
            pool,
            Subtask(pool, "Under pool: unassigned", null)
        );
        await db.SaveChangesAsync();

        var titles = (
            (await new TaskRepository(db).SearchAsync(TenantId, new TaskQueryFilter(), 1, 50, Employee)).Items
        )
            .Select(t => t.Title.Value)
            .Order()
            .ToList();

        Assert.Equal(
            [
                "Mine",
                "Office pool",
                "Under colleague: assigned to me",
                "Under mine: assigned to colleague",
                "Under mine: grandchild",
                "Under mine: unassigned",
                "Under pool: unassigned",
            ],
            titles
        );
    }

    /// <summary>El filtro desenrolla madre y abuela: si se permiten más niveles, hay que extenderlo.</summary>
    [Fact]
    public void The_visibility_rule_covers_every_subtask_level() => Assert.Equal(2, TaskItem.MaxDepth);

    private static TaskItem Subtask(TaskItem parent, string title, Guid? assignee) =>
        TaskItem
            .CreateSubtask(
                parent,
                Admin,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                assignee,
                DateTime.UtcNow
            )
            .Value;

    private static TasksDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

    private static async Task<TasksDbContext> SeedAsync()
    {
        var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );
        db.Tasks.AddRange(
            NewTask("Office pool", Admin, null, null),
            NewTask("Colleague private", Admin, null, Colleague),
            NewTask("Mine", Admin, null, Employee),
            NewTask("Created by me for a colleague", Employee, null, Colleague),
            NewTask("My client task", Admin, EmployeeClient, null),
            NewTask("Colleague client task", Admin, ColleagueClient, Colleague)
        );
        db.Set<CustomerAssignmentProjection>()
            .AddRange(
                CustomerAssignmentProjection.Create(TenantId, EmployeeClient, Employee, DateTime.UtcNow),
                CustomerAssignmentProjection.Create(TenantId, ColleagueClient, Colleague, DateTime.UtcNow)
            );
        await db.SaveChangesAsync();
        return db;
    }

    private static TaskItem NewTask(string title, Guid createdBy, Guid? customerId, Guid? assignee) =>
        TaskItem
            .Create(
                TenantId,
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

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
