using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.Domain;

/// <summary>
/// <c>TaskReference</c> es un tipo owned: cada fila necesita su propia instancia. La subtarea recibía la del padre y EF
/// intentaba moverla de fila ("TaskReference.TaskItemId is part of a key"): agregar una subtarea fallaba siempre.
/// </summary>
public sealed class TaskItemOwnedReferenceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_subtask_is_saved_next_to_its_parent_with_the_same_client_and_tax_year()
    {
        await using var db = NewDb();
        var parent = NewTask(TaskReference.Create(Guid.NewGuid(), 2025).Value);
        db.Tasks.Add(parent);
        await db.SaveChangesAsync();

        var subtask = TaskItem
            .CreateSubtask(
                parent,
                UserId,
                TaskTitle.Create("Check W-2 totals").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                null,
                Now
            )
            .Value;
        db.Tasks.Add(subtask);
        await db.SaveChangesAsync();

        Assert.NotSame(parent.Reference, subtask.Reference);
        Assert.Equal(parent.Reference.CustomerId, subtask.Reference.CustomerId);
        Assert.Equal(2025, subtask.Reference.TaxYear);
        Assert.Equal(1, parent.OpenSubtaskCount);
    }

    /// <summary><c>TaskReference.None</c> es una sola instancia estática: dos tareas sin cliente no pueden compartirla.</summary>
    [Fact]
    public async Task Two_tasks_without_a_client_are_saved_in_the_same_unit_of_work()
    {
        await using var db = NewDb();

        db.Tasks.Add(NewTask(TaskReference.None));
        db.Tasks.Add(NewTask(TaskReference.None));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Tasks.IgnoreQueryFilters().CountAsync());
    }

    private static TaskItem NewTask(TaskReference reference) =>
        TaskItem
            .Create(
                TenantId,
                UserId,
                TaskTitle.Create("Review 1040").Value,
                null,
                TaskPriority.Normal,
                reference,
                null,
                null,
                UserId,
                Now
            )
            .Value;

    private static TasksDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
