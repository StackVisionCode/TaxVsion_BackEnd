using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

/// <summary>
/// El formulario de edición completo: lo que no cambió se deja como está. <c>RowVersion</c> es la versión que el usuario
/// vio; si otro cambió la tarea después, nada se guarda.
/// </summary>
public sealed record UpdateTaskCommand(
    Guid TenantId,
    Guid TaskId,
    Guid ByUserId,
    bool HasManageAll,
    bool CanAssign,
    string Title,
    string? Description,
    TaskPriority Priority,
    DateOnly? DueOn,
    bool IsStatutory,
    string? StatutoryChangeReason,
    Guid? AssigneeUserId,
    int? EstimatedMinutes,
    Guid? CustomerId,
    int? TaxYear,
    string? RowVersion,
    bool CanViewAllCustomers = true
);

/// <summary>Todos los cambios del panel en una transacción (D-27), con la versión de la tarea (D-18).</summary>
public static class UpdateTaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        UpdateTaskCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IOfficeTimeZone zones,
        ITaskCustomerAccess customers,
        CancellationToken ct
    )
    {
        var form = await TaskEditForm.ParseAsync(command, zones, ct);
        if (form.IsFailure)
            return Result.Failure<TaskResponse>(form.Error);

        var loaded = await LoadAsync(command, tasks, customers, ct);
        if (loaded.IsFailure)
            return Result.Failure<TaskResponse>(loaded.Error);

        var task = loaded.Value;
        var previousDueAtUtc = task.Due?.DueAtUtc;
        var applied = form.Value.ApplyTo(task, command.ByUserId, DateTime.UtcNow);
        if (applied.IsFailure)
            return Result.Failure<TaskResponse>(applied.Error);

        if (applied.Value.ReferenceChanged)
            await InheritDownAsync(task, tasks, ct);
        if (applied.Value.Any)
            await unitOfWork.SaveChangesAsync(ct);
        if (applied.Value.DueChanged)
            await AnnounceDueAsync(task, previousDueAtUtc, command.ByUserId, bus, correlation);
        return Result.Success(TaskResponse.From(task));
    }

    private static async Task<Result<TaskItem>> LoadAsync(
        UpdateTaskCommand command,
        ITaskRepository tasks,
        ITaskCustomerAccess customers,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return found;

        var task = found.Value;
        if (!TaskAccessPolicy.CanMutate(task, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskItem>(TaskErrors.Forbidden);
        if (!command.CanAssign && command.AssigneeUserId != task.AssigneeUserId)
            return Result.Failure<TaskItem>(TaskErrors.Forbidden);
        // Solo el cliente nuevo se comprueba: el que ya tiene la tarea no cambia.
        if (
            command.CustomerId is { } customer
            && customer != task.Reference.CustomerId
            && !await customers.CanLinkAsync(task.TenantId, command.ByUserId, command.CanViewAllCustomers, customer, ct)
        )
            return Result.Failure<TaskItem>(TaskErrors.Reference.CustomerNotVisible);

        // La versión vista se compara aquí y otra vez al guardar: entre la lectura y el guardado puede cambiar.
        if (command.RowVersion is { Length: > 0 } seen && task.RowVersion.Length > 0)
        {
            var expected = Convert.FromBase64String(seen);
            if (!expected.AsSpan().SequenceEqual(task.RowVersion))
                return Result.Failure<TaskItem>(TaskErrors.StaleVersion);
            tasks.ExpectVersion(task, expected);
        }
        return Result.Success(task);
    }

    /// <summary>Las subtareas, a cualquier profundidad, toman el cliente y el año nuevos de la raíz.</summary>
    private static async Task InheritDownAsync(TaskItem root, ITaskRepository tasks, CancellationToken ct)
    {
        IReadOnlyCollection<Guid> level = [root.Id];
        while (level.Count > 0)
        {
            var childIds = await tasks.ListChildIdsAsync(root.TenantId, level, ct);
            if (childIds.Count == 0)
                return;
            foreach (var child in await tasks.ListByIdsAsync(root.TenantId, childIds, ct))
                child.InheritReference(root.Reference);
            level = [.. childIds];
        }
    }

    private static async Task AnnounceDueAsync(
        TaskItem task,
        DateTime? previousDueAtUtc,
        Guid byUserId,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        await bus.PublishAsync(
            ChangeTaskDueHandler.BuildEvent(task, previousDueAtUtc, byUserId, correlation.CorrelationId)
        );
        if (task.Due is not null)
            await bus.PublishAsync(TaskReminderContracts.Moved(task, correlation.CorrelationId));
    }
}
