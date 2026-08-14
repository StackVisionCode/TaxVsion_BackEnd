using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Templates.Commands;

public sealed record CreateTaskTemplateCommand(
    Guid TenantId,
    Guid ByUserId,
    string? Name,
    string? Description,
    string? RecurrenceRule,
    string? RecurrenceTimeZoneId,
    RecurrenceMode RecurrenceMode,
    IReadOnlyList<TaskTemplateStepDraft> Steps
);

public static class CreateTaskTemplateHandler
{
    public static async Task<Result<TaskTemplateResponse>> Handle(
        CreateTaskTemplateCommand command,
        ITaskTemplateRepository templates,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var created = TaskTemplate.Create(
            command.TenantId,
            command.ByUserId,
            command.Name,
            command.Description,
            DateTime.UtcNow
        );
        if (created.IsFailure)
            return Result.Failure<TaskTemplateResponse>(created.Error);

        var recurrence = TaskTemplateStepFactory.ApplyRecurrence(
            created.Value,
            command.RecurrenceRule,
            command.RecurrenceTimeZoneId,
            command.RecurrenceMode
        );
        if (recurrence.IsFailure)
            return Result.Failure<TaskTemplateResponse>(recurrence.Error);

        var applied = TaskTemplateStepFactory.ApplyTo(created.Value, command.Steps);
        if (applied.IsFailure)
            return Result.Failure<TaskTemplateResponse>(applied.Error);

        templates.Add(created.Value);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(TaskTemplateResponse.From(created.Value));
    }
}
