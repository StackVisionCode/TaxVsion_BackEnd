using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Tasks.Application.Series.Commands;

public sealed record CreateTaskSeriesCommand(
    Guid TenantId,
    Guid ByUserId,
    string? Title,
    string? Description,
    TaskPriority Priority,
    Guid? CustomerId,
    int? TaxYear,
    decimal? EstimatedHours,
    Guid AssigneeUserId,
    bool IsStatutory,
    string? Rule,
    string? TimeZoneId,
    RecurrenceMode Mode,
    DateTime AnchorUtc,
    DateTime? EndsAtUtc,
    int? MaxOccurrences,
    DateOnly? AnchorOn = null
);

/// <summary>
/// Crear la serie materializa su primera ocurrencia en la misma transacción: una serie activa sin
/// tarea abierta es un estado que nadie sabría interpretar. La primera ocurrencia pide su
/// recordatorio; sin <c>TaskAssigned</c> mientras D-TSK-1 siga abierta.
/// </summary>
public static class CreateTaskSeriesHandler
{
    public static async Task<Result<TaskSeriesResponse>> Handle(
        CreateTaskSeriesCommand command,
        ITaskSeriesRepository seriesRepository,
        ITaskSeriesMaterializer materializer,
        IUnitOfWork unitOfWork,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        command = await WithCivilAnchorAsync(command, zones, ct);
        var draft = BuildDraft(command);
        if (draft.IsFailure)
            return Result.Failure<TaskSeriesResponse>(draft.Error);

        var rule = RecurrenceRule.Create(command.Rule, command.TimeZoneId);
        if (rule.IsFailure)
            return Result.Failure<TaskSeriesResponse>(rule.Error);

        var created = TaskSeries.Create(
            command.TenantId,
            command.ByUserId,
            rule.Value,
            command.Mode,
            BuildBlueprint(command, draft.Value),
            command.AnchorUtc,
            command.EndsAtUtc,
            command.MaxOccurrences,
            DateTime.UtcNow
        );
        if (created.IsFailure)
            return Result.Failure<TaskSeriesResponse>(created.Error);

        seriesRepository.Add(created.Value);
        var first = await materializer.MaterializeFirstAsync(created.Value, ct);
        if (first.IsFailure)
            return Result.Failure<TaskSeriesResponse>(first.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskSeriesResponse.From(created.Value));
    }

    /// <summary>La serie no tiene vencimiento propio: cada ocurrencia lo calcula la regla.</summary>
    /// <summary>Con fecha civil (P4/P24): vence al final de ese día y la regla cuenta en la zona de la oficina.</summary>
    private static async Task<CreateTaskSeriesCommand> WithCivilAnchorAsync(
        CreateTaskSeriesCommand command,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        if (command.AnchorOn is not { } civil)
            return command;

        var zone = await zones.ResolveAsync(command.TenantId, ct);
        return command with { AnchorUtc = LocalDay.EndUtc(civil, zone), TimeZoneId = zone.Id };
    }

    private static Result<TaskDraft> BuildDraft(CreateTaskSeriesCommand command) =>
        TaskDraft.From(
            command.Title,
            command.Description,
            null,
            null,
            false,
            command.EstimatedHours,
            command.CustomerId,
            command.TaxYear
        );

    private static TaskItemBlueprint BuildBlueprint(CreateTaskSeriesCommand command, TaskDraft draft) =>
        new()
        {
            Title = draft.Title,
            Description = draft.Description,
            Priority = command.Priority,
            Reference = draft.Reference,
            Estimated = draft.Estimated,
            AssigneeUserId = command.AssigneeUserId,
            IsStatutory = command.IsStatutory,
        };
}
