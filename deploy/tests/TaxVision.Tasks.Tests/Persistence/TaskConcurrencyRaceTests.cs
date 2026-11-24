using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// Las dos carreras conocidas, contra SQL Server: cerrar dos subtareas a la vez (V-08) no pierde ni duplica el contador
/// de la madre; dar de alta una espera mientras se cierra la predecesora (V-02) deja una desviación que el
/// reconciliador encuentra.
/// </summary>
public sealed class TaskConcurrencyRaceTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=TaxVision_Tasks;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static TasksDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenantContext()
        );

    [Fact]
    public async Task Closing_two_subtasks_at_once_never_loses_or_doubles_the_parent_count()
    {
        var tenant = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var parent = NewTask(tenant, owner, "Prepare 1040");
        var first = Sub(parent, owner, "Collect W-2");
        var second = Sub(parent, owner, "Collect 1099");
        await SeedAsync(parent, first, second);

        await using var a = CreateContext();
        await using var b = CreateContext();
        var parentInA = (await new TaskRepository(a).GetByIdAsync(tenant, parent.Id)).Value;
        var parentInB = (await new TaskRepository(b).GetByIdAsync(tenant, parent.Id)).Value;

        parentInA.RegisterSubtaskClosed();
        await a.SaveChangesAsync();
        parentInB.RegisterSubtaskClosed();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => b.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        await using var check = CreateContext();
        var saved = (await new TaskRepository(check).GetByIdAsync(tenant, parent.Id)).Value;
        Assert.Equal(1, saved.OpenSubtaskCount);
    }

    [Fact]
    public async Task A_wait_added_while_its_predecessor_closes_is_caught_by_the_reconciler()
    {
        var tenant = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var predecessor = NewTask(tenant, owner, "Review documents");
        var successor = NewTask(tenant, owner, "File the return");
        await SeedAsync(predecessor, successor);

        // A empieza a añadir la espera; B cierra la predecesora sin ver aún esa arista.
        await using var a = CreateContext();
        var successorInA = (await new TaskRepository(a).GetByIdAsync(tenant, successor.Id)).Value;
        a.TaskDependencies.Add(
            TaskDependency.Create(tenant, successor.Id, predecessor.Id, owner, DateTime.UtcNow).Value
        );
        successorInA.RegisterBlockerAdded();
        await using (var b = CreateContext())
        {
            var predecessorInB = (await new TaskRepository(b).GetByIdAsync(tenant, predecessor.Id)).Value;
            Assert.True(predecessorInB.Complete(owner, DateTime.UtcNow).IsSuccess);
            await b.SaveChangesAsync();
        }
        await a.SaveChangesAsync();

        await using var check = CreateContext();
        var mismatches = await new TaskDependencyRepository(check).ListCounterMismatchesAsync(5000);
        var drift = Assert.Single(mismatches, m => m.TaskId == successor.Id);
        Assert.Equal((1, 0), (drift.StoredBlockers, drift.ActualBlockers));
    }

    private static async Task SeedAsync(params TaskItem[] tasks)
    {
        await using var seed = CreateContext();
        seed.Tasks.AddRange(tasks);
        await seed.SaveChangesAsync();
    }

    private static TaskItem NewTask(Guid tenant, Guid owner, string title) =>
        TaskItem
            .Create(
                tenant,
                owner,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                owner,
                DateTime.UtcNow
            )
            .Value;

    private static TaskItem Sub(TaskItem parent, Guid owner, string title) =>
        TaskItem
            .CreateSubtask(
                parent,
                owner,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                owner,
                DateTime.UtcNow
            )
            .Value;
}
