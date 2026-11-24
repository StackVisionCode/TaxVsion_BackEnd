using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// Cambiar el cliente y el año de una tarea: solo la tarea raíz (las subtareas heredan) y sin cambiar de cliente
/// mientras se espera a ese cliente.
/// </summary>
public sealed class TaskReferenceChangeTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Acme = Guid.NewGuid();
    private static readonly Guid Globex = Guid.NewGuid();

    [Fact]
    public void A_root_task_takes_the_new_customer_and_year_and_records_it()
    {
        var task = NewTask(null, null);

        var changed = task.ChangeReference(Reference(Acme, 2025), Owner, DateTime.UtcNow);

        Assert.True(changed.IsSuccess);
        Assert.Equal(((Guid?)Acme, (int?)2025), (task.Reference.CustomerId, task.Reference.TaxYear));
        var raised = Assert.IsType<TaskReferenceChangedDomainEvent>(Assert.Single(task.DomainEvents));
        Assert.Equal(
            (null, Acme, null, 2025),
            (raised.PreviousCustomerId, raised.CustomerId, raised.PreviousTaxYear, raised.TaxYear)
        );
    }

    [Fact]
    public void A_subtask_follows_its_parent_and_cannot_change_alone()
    {
        var parent = NewTask(Acme, 2025);
        var child = TaskItem
            .CreateSubtask(
                parent,
                Owner,
                TaskTitle.Create("Collect W-2").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                Owner,
                DateTime.UtcNow
            )
            .Value;

        var changed = child.ChangeReference(Reference(Globex, 2025), Owner, DateTime.UtcNow);

        Assert.Equal(TaskErrors.Reference.FollowsParent, changed.Error);
    }

    [Fact]
    public void While_waiting_on_the_client_the_customer_stays_but_the_year_can_change()
    {
        var task = NewTask(Acme, 2025);
        task.MoveToWaitingOnClient(ClientRequestNote.Create("W-2").Value, null, Owner, DateTime.UtcNow);

        var otherCustomer = task.ChangeReference(Reference(Globex, 2025), Owner, DateTime.UtcNow);
        var otherYear = task.ChangeReference(Reference(Acme, 2024), Owner, DateTime.UtcNow);

        Assert.Equal(TaskErrors.Reference.CustomerLockedWhileWaiting, otherCustomer.Error);
        Assert.True(otherYear.IsSuccess);
        Assert.Equal(2024, task.Reference.TaxYear);
    }

    [Fact]
    public void The_same_reference_changes_nothing()
    {
        var task = NewTask(Acme, 2025);

        task.ChangeReference(Reference(Acme, 2025), Owner, DateTime.UtcNow);

        Assert.Empty(task.DomainEvents);
    }

    private static TaskReference Reference(Guid? customer, int? year) => TaskReference.Create(customer, year).Value;

    private static TaskItem NewTask(Guid? customer, int? year)
    {
        var task = TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                Reference(customer, year),
                null,
                null,
                Owner,
                DateTime.UtcNow
            )
            .Value;
        task.ClearDomainEvents();
        return task;
    }
}
