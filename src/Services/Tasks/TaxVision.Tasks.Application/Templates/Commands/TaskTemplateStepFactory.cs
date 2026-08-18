using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Templates.Commands;

public sealed record TaskTemplateStepDraft(
    int Order,
    string? Title,
    string? Description,
    TaskPriority Priority,
    decimal? EstimatedHours,
    int DueOffsetDays,
    bool IsStatutory,
    int? DependsOnStepOrder,
    int? ParentStepOrder,
    string? SuggestedRoleName
);

/// <summary>
/// Traduce el borrador del request a pasos de dominio. Compartido por crear, editar e instalar el
/// catálogo estándar, porque el guion se valida entero de una vez en los tres casos.
/// </summary>
public static class TaskTemplateStepFactory
{
    public static Result ApplyTo(TaskTemplate template, IReadOnlyList<TaskTemplateStepDraft> drafts)
    {
        var steps = new List<TaskTemplateStep>(drafts.Count);

        foreach (var draft in drafts)
        {
            var step = Build(draft);
            if (step.IsFailure)
                return Result.Failure(step.Error);

            steps.Add(step.Value);
        }

        return template.ReplaceSteps(steps, DateTime.UtcNow);
    }

    /// <summary>
    /// Sin regla la plantilla es un grafo; el modo sólo importa cuando la hay, así que no se valida
    /// por separado.
    /// </summary>
    public static Result ApplyRecurrence(TaskTemplate template, string? rule, string? timeZoneId, RecurrenceMode mode)
    {
        if (string.IsNullOrWhiteSpace(rule))
            return template.SetRecurrence(null, mode, DateTime.UtcNow);

        var parsed = RecurrenceRule.Create(rule, timeZoneId);

        return parsed.IsFailure
            ? Result.Failure(parsed.Error)
            : template.SetRecurrence(parsed.Value, mode, DateTime.UtcNow);
    }

    private static Result<TaskTemplateStep> Build(TaskTemplateStepDraft draft)
    {
        var title = TaskTitle.Create(draft.Title);
        if (title.IsFailure)
            return Result.Failure<TaskTemplateStep>(title.Error);

        var description = string.IsNullOrWhiteSpace(draft.Description)
            ? null
            : TaskDescription.Create(draft.Description);
        if (description is { IsFailure: true })
            return Result.Failure<TaskTemplateStep>(description.Error);

        var estimated = draft.EstimatedHours is { } hours ? EstimatedHours.Create(hours) : null;
        if (estimated is { IsFailure: true })
            return Result.Failure<TaskTemplateStep>(estimated.Error);

        return TaskTemplateStep.Create(
            draft.Order,
            title.Value,
            description?.Value,
            draft.Priority,
            estimated?.Value,
            draft.DueOffsetDays,
            draft.IsStatutory,
            draft.DependsOnStepOrder,
            draft.ParentStepOrder,
            draft.SuggestedRoleName
        );
    }
}
