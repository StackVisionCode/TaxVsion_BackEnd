using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>
/// Los VOs ya validados de un alta. Vive acá y no dentro de un handler porque el alta de una tarea
/// raíz y la de una subtarea construyen exactamente lo mismo.
/// </summary>
public sealed record TaskDraft(
    TaskTitle Title,
    TaskDescription? Description,
    DueDate? Due,
    EstimatedHours? Estimated,
    TaskReference Reference
)
{
    public static Result<TaskDraft> From(
        string? title,
        string? description,
        DateTime? dueAtUtc,
        string? dueTimeZoneId,
        bool dueIsStatutory,
        decimal? estimatedHours,
        Guid? customerId,
        int? taxYear
    )
    {
        var parsedTitle = TaskTitle.Create(title);
        if (parsedTitle.IsFailure)
            return Result.Failure<TaskDraft>(parsedTitle.Error);

        var parsedDescription = Optional(description, TaskDescription.Create);
        if (parsedDescription.IsFailure)
            return Result.Failure<TaskDraft>(parsedDescription.Error);

        var parsedDue = dueAtUtc is { } due ? DueDate.Create(due, dueTimeZoneId, dueIsStatutory) : null;
        if (parsedDue is { IsFailure: true })
            return Result.Failure<TaskDraft>(parsedDue.Error);

        var parsedEstimated = estimatedHours is { } hours ? EstimatedHours.Create(hours) : null;
        if (parsedEstimated is { IsFailure: true })
            return Result.Failure<TaskDraft>(parsedEstimated.Error);

        var parsedReference = TaskReference.Create(customerId, taxYear);
        if (parsedReference.IsFailure)
            return Result.Failure<TaskDraft>(parsedReference.Error);

        return Result.Success(
            new TaskDraft(
                parsedTitle.Value,
                parsedDescription.Value,
                parsedDue?.Value,
                parsedEstimated?.Value,
                parsedReference.Value
            )
        );
    }

    /// <summary>Vacío significa «sin valor», no «valor inválido».</summary>
    private static Result<TaskDescription?> Optional(string? value, Func<string?, Result<TaskDescription>> create)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Success<TaskDescription?>(null);

        var created = create(value);
        return created.IsFailure
            ? Result.Failure<TaskDescription?>(created.Error)
            : Result.Success<TaskDescription?>(created.Value);
    }
}
