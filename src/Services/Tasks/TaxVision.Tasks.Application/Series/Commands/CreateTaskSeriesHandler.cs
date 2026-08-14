using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

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
    int? MaxOccurrences
);

/// <summary>
/// Crear la serie materializa su primera ocurrencia en la misma transacción: una serie activa sin
/// tarea abierta es un estado que nadie sabría interpretar.
/// </summary>
public static class CreateTaskSeriesHandler
{
    public static async Task<Result<TaskSeriesResponse>> Handle(
        CreateTaskSeriesCommand command,
        ITaskSeriesRepository seriesRepository,
        ITaskSeriesMaterializer materializer,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
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
        var first = await materializer.MaterializeNextAsync(created.Value, null, null, ct);
        if (first.IsFailure)
            return Result.Failure<TaskSeriesResponse>(first.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskSeriesResponse.From(created.Value));
    }

    /// <summary>La serie no tiene vencimiento propio: cada ocurrencia lo calcula la regla.</summary>
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
