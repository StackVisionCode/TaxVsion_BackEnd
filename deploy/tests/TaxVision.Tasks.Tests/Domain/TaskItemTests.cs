using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

/// <summary>Decisiones del agregado contra datos propios: sin BD y sin grafo de dependencias real.</summary>
public sealed class TaskItemTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Now = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    // ── Bloqueo ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_with_open_blockers_fails()
    {
        var task = NewTask();
        task.RegisterBlockerAdded();

        var result = task.Complete(UserId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.BlockedByDependencies", result.Error.Code);
        Assert.NotEqual(TaskItemStatus.Completed, task.Status);
    }

    [Fact]
    public void Complete_with_open_subtasks_fails()
    {
        var task = NewTask();
        task.RegisterSubtaskOpened();

        var result = task.Complete(UserId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.HasOpenSubtasks", result.Error.Code);
    }

    /// <summary>Con un <c>bool</c> en vez de contador, el primer bloqueador resuelto desbloquearía la tarea.</summary>
    [Fact]
    public void Resolving_one_of_two_blockers_keeps_the_task_blocked()
    {
        var task = NewTask();
        task.RegisterBlockerAdded();
        task.RegisterBlockerAdded();

        task.RegisterBlockerResolved(Now);
        Assert.True(task.IsBlocked);
        Assert.Empty(task.DomainEvents.OfType<TaskUnblockedDomainEvent>());

        task.RegisterBlockerResolved(Now);
        Assert.False(task.IsBlocked);
        Assert.Single(task.DomainEvents.OfType<TaskUnblockedDomainEvent>());
    }

    [Fact]
    public void Resolving_a_blocker_at_zero_does_not_go_negative()
    {
        var task = NewTask();

        task.RegisterBlockerResolved(Now);

        Assert.Equal(0, task.OpenBlockerCount);
        Assert.False(task.IsBlocked);
        Assert.Empty(task.DomainEvents.OfType<TaskUnblockedDomainEvent>());
    }

    [Fact]
    public void Start_fails_while_blocked_and_succeeds_once_released()
    {
        var task = NewTask();
        task.RegisterBlockerAdded();

        Assert.True(task.Start(UserId, Now).IsFailure);

        task.RegisterBlockerResolved(Now);

        Assert.True(task.Start(UserId, Now).IsSuccess);
        Assert.Equal(TaskItemStatus.InProgress, task.Status);
    }

    [Fact]
    public void Reopening_a_resolved_blocker_raises_the_counter_again()
    {
        var task = NewTask();
        task.RegisterBlockerAdded();
        task.RegisterBlockerResolved(Now);

        task.RegisterBlockerReopened();

        Assert.True(task.IsBlocked);
        Assert.Equal(1, task.OpenBlockerCount);
        Assert.True(task.Start(UserId, Now).IsFailure);
    }

    // ── Idempotencia ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_twice_succeeds_twice_but_emits_one_event()
    {
        var task = NewTask();

        Assert.True(task.Complete(UserId, Now).IsSuccess);
        Assert.True(task.Complete(UserId, Now.AddMinutes(5)).IsSuccess);

        Assert.Single(task.DomainEvents.OfType<TaskCompletedDomainEvent>());
        Assert.Equal(Now, task.CompletedAtUtc);
    }

    // ── Tabla de transiciones ─────────────────────────────────────────────────────────────

    [Fact]
    public void NotStarted_can_be_completed_without_starting_first()
    {
        var task = NewTask();

        Assert.Equal(TaskItemStatus.NotStarted, task.Status);
        Assert.True(task.Complete(UserId, Now).IsSuccess);
        Assert.Equal(TaskItemStatus.Completed, task.Status);
        Assert.Null(task.StartedAtUtc);
    }

    [Theory]
    [InlineData(nameof(TaskItem.Start))]
    [InlineData(nameof(TaskItem.Assign))]
    [InlineData(nameof(TaskItem.ChangeDue))]
    public void A_completed_task_rejects_further_transitions(string operation)
    {
        var task = NewTask();
        task.Complete(UserId, Now);

        var result = Invoke(task, operation);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void A_cancelled_task_cannot_be_put_on_hold_waiting_for_the_client()
    {
        var task = NewTask();
        task.Cancel("duplicada", UserId, Now);

        var result = task.MoveToWaitingOnClient(Note("falta W-2"), null, UserId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.WaitingOnClient.TaskClosed", result.Error.Code);
    }

    /// <summary>El cliente manda el W-2 pero olvida el 1099: el bucle es legal y puede repetirse.</summary>
    [Fact]
    public void The_waiting_on_client_loop_can_run_more_than_once()
    {
        var task = NewTask();
        task.Start(UserId, Now);

        Assert.True(task.MoveToWaitingOnClient(Note("falta W-2"), null, UserId, Now).IsSuccess);
        Assert.True(task.Start(UserId, Now).IsSuccess);
        Assert.True(task.MoveToWaitingOnClient(Note("falta 1099-INT"), null, UserId, Now).IsSuccess);
        Assert.True(task.Start(UserId, Now).IsSuccess);

        Assert.Equal(TaskItemStatus.InProgress, task.Status);
        Assert.Equal("falta 1099-INT", task.ExpectedItems!.Value);
    }

    [Fact]
    public void WaitingOnClient_can_be_completed_without_going_back_to_InProgress()
    {
        var task = NewTask();
        task.MoveToWaitingOnClient(Note("falta W-2"), null, UserId, Now);

        Assert.True(task.Complete(UserId, Now).IsSuccess);
        Assert.Equal(TaskItemStatus.Completed, task.Status);
    }

    // ── Reopen y su estado destino ────────────────────────────────────────────────────────

    [Fact]
    public void Reopen_of_a_worked_task_resumes_InProgress()
    {
        var task = NewTask();
        task.Start(UserId, Now);
        task.Complete(UserId, Now.AddHours(1));

        Assert.True(task.Reopen(UserId, Now.AddHours(2)).IsSuccess);

        Assert.Equal(TaskItemStatus.InProgress, task.Status);
        Assert.Null(task.CompletedAtUtc);
    }

    [Fact]
    public void Reopen_of_a_task_cancelled_before_starting_resumes_NotStarted()
    {
        var task = NewTask();
        task.Cancel("ya no aplica", UserId, Now);

        Assert.True(task.Reopen(UserId, Now.AddHours(1)).IsSuccess);

        Assert.Equal(TaskItemStatus.NotStarted, task.Status);
        Assert.Null(task.CompletedAtUtc);
    }

    /// <summary>
    /// Volver a <see cref="TaskItemStatus.WaitingOnClient"/> dejaría la tarea esperando algo que nadie
    /// pidió: si hace falta otra petición se hace de nuevo y sale un correo nuevo.
    /// </summary>
    [Fact]
    public void Reopen_never_resumes_WaitingOnClient()
    {
        var task = NewTask();
        task.Start(UserId, Now);
        task.MoveToWaitingOnClient(Note("falta W-2"), null, UserId, Now);
        task.Complete(UserId, Now.AddHours(1));

        task.Reopen(UserId, Now.AddHours(2));

        Assert.Equal(TaskItemStatus.InProgress, task.Status);
        Assert.NotEqual(TaskItemStatus.WaitingOnClient, task.Status);
    }

    [Fact]
    public void Reopen_on_an_open_task_is_an_invalid_transition()
    {
        var task = NewTask();

        var result = task.Reopen(UserId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.InvalidTransition", result.Error.Code);
    }

    // ── Petición al cliente ───────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>task.client_responded.v1</c> se publica semanas después con la tarea como única fuente: sin
    /// el solicitante persistido ese evento no se puede construir.
    /// </summary>
    [Fact]
    public void MoveToWaitingOnClient_persists_who_asked_and_when()
    {
        var task = NewTask();
        var clientDue = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc);

        task.MoveToWaitingOnClient(Note("falta W-2 y 1099-INT"), clientDue, UserId, Now);

        Assert.Equal(UserId, task.ClientRequestedByUserId);
        Assert.Equal(Now, task.ClientRequestedAtUtc);
        Assert.Equal(clientDue, task.ClientDueAtUtc);
        Assert.Equal(
            "falta W-2 y 1099-INT",
            task.DomainEvents.OfType<TaskMovedToWaitingOnClientDomainEvent>().Single().ExpectedItems
        );
    }

    [Fact]
    public void The_client_due_date_is_independent_from_the_task_due_date()
    {
        var task = NewTask(
            due: DueDate.Create(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc), "America/New_York", true).Value
        );
        var clientDue = new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc);

        task.MoveToWaitingOnClient(Note("falta W-2"), clientDue, UserId, Now);

        Assert.Equal(clientDue, task.ClientDueAtUtc);
        Assert.Equal(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc), task.Due!.DueAtUtc);
    }

    [Fact]
    public void A_client_due_date_that_is_not_utc_is_rejected()
    {
        var task = NewTask();

        var result = task.MoveToWaitingOnClient(Note("falta W-2"), new DateTime(2026, 4, 3), UserId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.WaitingOnClient.ClientDueNotUtc", result.Error.Code);
    }

    // ── Asignación ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Assign_records_the_previous_assignee_so_both_sides_can_be_notified()
    {
        var task = NewTask(assigneeUserId: UserId);
        var reviewer = Guid.Parse("33333333-3333-3333-3333-333333333333");

        task.Assign(reviewer, UserId, Now);

        var assigned = task.DomainEvents.OfType<TaskAssignedDomainEvent>().Last();
        Assert.Equal(reviewer, assigned.AssigneeUserId);
        Assert.Equal(UserId, assigned.PreviousAssigneeUserId);
    }

    [Fact]
    public void Assigning_the_same_user_again_is_a_no_op()
    {
        var task = NewTask(assigneeUserId: UserId);

        Assert.True(task.Assign(UserId, UserId, Now).IsSuccess);
        Assert.Empty(task.DomainEvents.OfType<TaskAssignedDomainEvent>());
    }

    [Fact]
    public void Unassign_leaves_nobody_responsible()
    {
        var task = NewTask(assigneeUserId: UserId);

        Assert.True(task.Unassign(UserId, Now).IsSuccess);

        Assert.Null(task.AssigneeUserId);
        Assert.Single(task.DomainEvents.OfType<TaskUnassignedDomainEvent>());
    }

    // ── Creación ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_requires_both_tenant_and_creating_user()
    {
        var title = TaskTitle.Create("Preparar 1040").Value;

        var withoutTenant = TaskItem.Create(
            Guid.Empty,
            UserId,
            title,
            null,
            TaskPriority.Normal,
            TaskReference.None,
            null,
            null,
            null,
            Now
        );
        var withoutUser = TaskItem.Create(
            TenantId,
            Guid.Empty,
            title,
            null,
            TaskPriority.Normal,
            TaskReference.None,
            null,
            null,
            null,
            Now
        );

        Assert.True(withoutTenant.IsFailure);
        Assert.True(withoutUser.IsFailure);
        Assert.Equal("Task.OwnerRequired", withoutTenant.Error.Code);
    }

    [Fact]
    public void Create_starts_NotStarted_unblocked_and_emits_one_created_event()
    {
        var task = NewTask();

        Assert.Equal(TaskItemStatus.NotStarted, task.Status);
        Assert.False(task.IsBlocked);
        Assert.Equal(0, task.OpenSubtaskCount);
        Assert.Equal(TenantId, task.TenantId);
        Assert.Single(task.DomainEvents.OfType<TaskCreatedDomainEvent>());
    }

    // ── Vencimiento estatutario ───────────────────────────────────────────────────────────

    [Fact]
    public void Postponing_a_statutory_due_without_a_reason_fails_and_keeps_the_date()
    {
        var task = NewTask(due: Due(Apr15, isStatutory: true));

        var result = task.ChangeDue(Due(Oct15, isStatutory: true), UserId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Due.StatutoryReasonRequired", result.Error.Code);
        Assert.Equal(Apr15, task.Due!.DueAtUtc);
    }

    [Fact]
    public void Postponing_a_statutory_due_with_a_reason_succeeds_and_the_reason_travels_in_the_event()
    {
        var task = NewTask(due: Due(Apr15, isStatutory: true));

        var result = task.ChangeDue(Due(Oct15, isStatutory: true), UserId, Now, "  Form 4868 extension filed  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(Oct15, task.Due!.DueAtUtc);
        var evt = task.DomainEvents.OfType<TaskDueChangedDomainEvent>().Single();
        Assert.Equal("Form 4868 extension filed", evt.StatutoryChangeReason);
    }

    /// <summary>Adelantarlo es margen interno de la firma: no afloja nada legal.</summary>
    [Fact]
    public void Bringing_a_statutory_due_forward_needs_no_reason()
    {
        var task = NewTask(due: Due(Oct15, isStatutory: true));

        var result = task.ChangeDue(Due(Apr15, isStatutory: true), UserId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Apr15, task.Due!.DueAtUtc);
    }

    /// <summary>
    /// La puerta de atrás: desmarcar lo estatutario y mover libre después. Sin este guard, la regla
    /// se esquiva en dos pasos.
    /// </summary>
    [Fact]
    public void Dropping_the_statutory_flag_without_a_reason_fails()
    {
        var task = NewTask(due: Due(Apr15, isStatutory: true));

        var result = task.ChangeDue(Due(Apr15, isStatutory: false), UserId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Due.StatutoryReasonRequired", result.Error.Code);
        Assert.True(task.Due!.IsStatutory);
    }

    [Fact]
    public void Clearing_a_statutory_due_without_a_reason_fails()
    {
        var task = NewTask(due: Due(Apr15, isStatutory: true));

        var result = task.ChangeDue(null, UserId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Due.StatutoryReasonRequired", result.Error.Code);
        Assert.NotNull(task.Due);
    }

    [Fact]
    public void Postponing_an_internal_due_needs_no_reason_and_records_none()
    {
        var task = NewTask(due: Due(Apr15, isStatutory: false));

        var result = task.ChangeDue(Due(Oct15, isStatutory: false), UserId, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(task.DomainEvents.OfType<TaskDueChangedDomainEvent>().Single().StatutoryChangeReason);
    }

    /// <summary>Una razón en un cambio que no la exige no se guarda: el audit sólo marca lo que importa.</summary>
    [Fact]
    public void A_reason_supplied_on_a_change_that_does_not_need_one_is_discarded()
    {
        var task = NewTask(due: Due(Oct15, isStatutory: true));

        task.ChangeDue(Due(Apr15, isStatutory: true), UserId, Now, "irrelevante");

        Assert.Null(task.DomainEvents.OfType<TaskDueChangedDomainEvent>().Single().StatutoryChangeReason);
    }

    [Fact]
    public void An_overlong_reason_fails()
    {
        var task = NewTask(due: Due(Apr15, isStatutory: true));

        var result = task.ChangeDue(
            Due(Oct15, isStatutory: true),
            UserId,
            Now,
            new string('x', TaskErrors.StatutoryChangeReasonMaxLength + 1)
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Due.StatutoryReasonTooLong", result.Error.Code);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static readonly DateTime Apr15 = new(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Oct15 = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static DueDate Due(DateTime dueAtUtc, bool isStatutory) =>
        DueDate.Create(dueAtUtc, "America/New_York", isStatutory).Value;

    private static TaskItem NewTask(Guid? assigneeUserId = null, DueDate? due = null) =>
        TaskItem
            .Create(
                TenantId,
                UserId,
                TaskTitle.Create("Preparar 1040 de Pérez").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                due,
                null,
                assigneeUserId,
                Now
            )
            .Value;

    private static ClientRequestNote Note(string value) => ClientRequestNote.Create(value).Value;

    private static BuildingBlocks.Results.Result Invoke(TaskItem task, string operation) =>
        operation switch
        {
            nameof(TaskItem.Start) => task.Start(UserId, Now),
            nameof(TaskItem.Assign) => task.Assign(UserId, UserId, Now),
            nameof(TaskItem.ChangeDue) => task.ChangeDue(null, UserId, Now),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unmapped operation."),
        };
}
