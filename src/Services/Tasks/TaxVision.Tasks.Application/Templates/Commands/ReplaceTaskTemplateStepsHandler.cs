using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Templates.Commands;

public sealed record ReplaceTaskTemplateStepsCommand(
    Guid TenantId,
    Guid TemplateId,
    string? Name,
    string? Description,
    string? RecurrenceRule,
    string? RecurrenceTimeZoneId,
    RecurrenceMode RecurrenceMode,
    IReadOnlyList<TaskTemplateStepDraft> Steps
);

/// <summary>
/// Editar una plantilla no toca las tareas que ya nacieron de ella: el encargo en curso sigue con el
/// guion con el que empezó. Cambiar el pasado sería reescribir trabajo que alguien ya hizo.
/// </summary>
public static class ReplaceTaskTemplateStepsHandler
{
    public static async Task<Result<TaskTemplateResponse>> Handle(
        ReplaceTaskTemplateStepsCommand command,
        ITaskTemplateRepository templates,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskTemplateResponse>(found.Error);

        var template = found.Value;
        var renamed = template.Rename(command.Name, command.Description, DateTime.UtcNow);
        if (renamed.IsFailure)
            return Result.Failure<TaskTemplateResponse>(renamed.Error);

        var recurrence = TaskTemplateStepFactory.ApplyRecurrence(
            template,
            command.RecurrenceRule,
            command.RecurrenceTimeZoneId,
            command.RecurrenceMode
        );
        if (recurrence.IsFailure)
            return Result.Failure<TaskTemplateResponse>(recurrence.Error);

        var applied = TaskTemplateStepFactory.ApplyTo(template, command.Steps);
        if (applied.IsFailure)
            return Result.Failure<TaskTemplateResponse>(applied.Error);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(TaskTemplateResponse.From(template));
    }
}
