using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Series.Commands;

/// <param name="StartsOn">Desde cuándo rige un calendario nuevo; por defecto, el día después de la tarea abierta.</param>
/// <param name="ApplyToOpenInstance">Copia título, descripción, prioridad y responsable a la tarea abierta.</param>
public sealed record UpdateTaskSeriesCommand(
    Guid TenantId,
    Guid SeriesId,
    Guid ByUserId,
    bool HasManageAll,
    string? Title,
    string? Description,
    TaskPriority Priority,
    Guid AssigneeUserId,
    bool IsStatutory,
    string? Rule,
    RecurrenceMode Mode,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    int? MaxOccurrences,
    bool ApplyToOpenInstance,
    Guid? CustomerId,
    string? RowVersion,
    bool CanViewAllCustomers = true
);

/// <summary>
/// Editar una serie sin terminarla y recrearla. Cambia las ocurrencias futuras; las cerradas no se tocan y la abierta
/// solo si se pide (su vencimiento nunca). Un calendario nuevo empieza en su primera fecha desde "Starts on".
/// </summary>
public static class UpdateTaskSeriesHandler
{
    public static async Task<Result<TaskSeriesResponse>> Handle(
        UpdateTaskSeriesCommand command,
        ITaskSeriesRepository seriesRepository,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IOfficeTimeZone zones,
        ITaskCustomerAccess customers,
        CancellationToken ct
    )
    {
        var found = await seriesRepository.GetByIdAsync(command.TenantId, command.SeriesId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskSeriesResponse>(found.Error);

        var series = found.Value;
        if (!TaskSeriesAccessPolicy.CanMutate(series, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskSeriesResponse>(TaskErrors.Forbidden);
        if (!await CanTakeClientAsync(command, series, customers, ct))
            return Result.Failure<TaskSeriesResponse>(TaskErrors.Reference.CustomerNotVisible);

        if (series.Status == SeriesStatus.Ended)
            return Result.Failure<TaskSeriesResponse>(TaskErrors.Series.AlreadyEnded);

        var zone = await zones.ResolveAsync(command.TenantId, ct);
        var changed = Apply(command, series, zone);
        if (changed.IsFailure)
            return Result.Failure<TaskSeriesResponse>(changed.Error);

        if (command.ApplyToOpenInstance)
        {
            var applied = await ApplyToOpenInstanceAsync(series, command.ByUserId, tasks, ct);
            if (applied.IsFailure)
                return Result.Failure<TaskSeriesResponse>(applied.Error);
        }

        if (command.RowVersion is { Length: > 0 } version && series.RowVersion.Length > 0)
            seriesRepository.ExpectVersion(series, Convert.FromBase64String(version));
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskSeriesResponse.From(series));
    }

    /// <summary>Solo un cliente nuevo se comprueba: el que ya tiene la serie no cambia.</summary>
    private static Task<bool> CanTakeClientAsync(
        UpdateTaskSeriesCommand command,
        TaskSeries series,
        ITaskCustomerAccess customers,
        CancellationToken ct
    )
    {
        var newClient = command.CustomerId == series.Blueprint.Reference.CustomerId ? null : command.CustomerId;
        return customers.AllowsAsync(command.TenantId, command.ByUserId, command.CanViewAllCustomers, newClient, ct);
    }

    /// <summary>El calendario primero: el fin se valida contra la ocurrencia en curso.</summary>
    private static Result Apply(UpdateTaskSeriesCommand command, TaskSeries series, ResolvedTimeZone zone)
    {
        var draft = TaskDraft.From(command.Title, command.Description, null, null, false, null, null, null);
        if (draft.IsFailure)
            return draft;

        var rescheduled = Reschedule(command, series, zone);
        if (rescheduled.IsFailure)
            return rescheduled;

        // El cliente nuevo vale para las próximas; el año sigue la regla de la serie.
        var reference = TaskReference.Create(command.CustomerId, series.Blueprint.Reference.TaxYear);
        if (reference.IsFailure)
            return reference;

        var blueprint = series.Blueprint with
        {
            Title = draft.Value.Title,
            Description = draft.Value.Description,
            Priority = command.Priority,
            AssigneeUserId = command.AssigneeUserId,
            IsStatutory = command.IsStatutory,
            Reference = reference.Value,
        };
        var endsAtUtc = command.EndsOn is { } endsOn ? LocalDay.EndUtc(endsOn, zone) : (DateTime?)null;
        return series.Revise(blueprint, endsAtUtc, command.MaxOccurrences);
    }

    /// <summary>Solo si cambió la regla o el modo. La regla se guarda en la zona de la oficina.</summary>
    private static Result Reschedule(UpdateTaskSeriesCommand command, TaskSeries series, ResolvedTimeZone zone)
    {
        var rule = RecurrenceRule.Create(command.Rule, zone.Id);
        if (rule.IsFailure)
            return rule;
        if (rule.Value.Value == series.Rule.Value && command.Mode == series.Mode)
            return Result.Success();

        var startsOn = command.StartsOn ?? DefaultStart(series, zone);
        var first = RecurrencePreview.FirstDueUtc(rule.Value, command.Mode, startsOn);
        if (first.IsFailure)
            return first;
        return series.Reschedule(rule.Value, command.Mode, first.Value);
    }

    /// <summary>El día después de la última fecha planificada, o hoy si es más tarde.</summary>
    private static DateOnly DefaultStart(TaskSeries series, ResolvedTimeZone zone)
    {
        var today = CivilDate.Of(DateTime.UtcNow, zone);
        if (series.GeneratedCount == 0)
            return today;
        var afterCurrent = CivilDate.Of(series.LastPlannedDueUtc ?? series.AnchorUtc, zone).AddDays(1);
        return afterCurrent > today ? afterCurrent : today;
    }

    private static async Task<Result> ApplyToOpenInstanceAsync(
        TaskSeries series,
        Guid byUserId,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        if (series.OpenInstanceId is not { } openId)
            return Result.Success();
        var found = await tasks.GetByIdAsync(series.TenantId, openId, ct);
        if (found.IsFailure || TaskItemStatuses.IsClosed(found.Value.Status))
            return Result.Success();

        var task = found.Value;
        var now = DateTime.UtcNow;
        var blueprint = series.Blueprint;
        var steps = new[]
        {
            task.ChangeTitle(blueprint.Title),
            task.ChangeDescription(blueprint.Description),
            task.ChangePriority(blueprint.Priority, byUserId, now),
            task.Assign(blueprint.AssigneeUserId, byUserId, now),
        };
        return steps.FirstOrDefault(s => s.IsFailure) ?? Result.Success();
    }
}
