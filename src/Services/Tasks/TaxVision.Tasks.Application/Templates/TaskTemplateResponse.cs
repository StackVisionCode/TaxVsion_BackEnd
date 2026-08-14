using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Application.Templates;

public sealed record TaskTemplateStepResponse(
    int Order,
    string Title,
    string? Description,
    TaskPriority Priority,
    decimal? EstimatedHours,
    int DueOffsetDays,
    bool IsStatutory,
    int? DependsOnStepOrder,
    int? ParentStepOrder,
    string? SuggestedRoleName
);

public sealed record TaskTemplateAttachmentResponse(
    Guid Id,
    Guid FileId,
    string DisplayName,
    string? ContentType,
    long SizeBytes,
    int? StepOrder
);

public sealed record TaskTemplateResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    string? RecurrenceRule,
    string? RecurrenceTimeZoneId,
    RecurrenceMode RecurrenceMode,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    IReadOnlyList<TaskTemplateStepResponse> Steps,
    IReadOnlyList<TaskTemplateAttachmentResponse> Attachments
)
{
    public static TaskTemplateResponse From(TaskTemplate template) =>
        new(
            template.Id,
            template.Name,
            template.Description,
            template.IsActive,
            template.Recurrence?.Value,
            template.Recurrence?.TimeZoneId,
            template.RecurrenceMode,
            template.CreatedAtUtc,
            template.UpdatedAtUtc,
            [
                .. template.Steps.Select(s => new TaskTemplateStepResponse(
                    s.Order,
                    s.Title.Value,
                    s.Description?.Value,
                    s.Priority,
                    s.Estimated?.Value,
                    s.DueOffsetDays,
                    s.IsStatutory,
                    s.DependsOnStepOrder,
                    s.ParentStepOrder,
                    s.SuggestedRoleName
                )),
            ],
            [
                .. template.Attachments.Select(a => new TaskTemplateAttachmentResponse(
                    a.Id,
                    a.FileId,
                    a.DisplayName,
                    a.ContentType,
                    a.SizeBytes,
                    a.StepOrder
                )),
            ]
        );
}

/// <summary>
/// Lo que dejó una aplicación: cuántas tareas y aristas, y cuál es el primer paso ejecutable. Una
/// plantilla recurrente devuelve <c>SeriesId</c> y una sola tarea —la primera ocurrencia—; las demás
/// las materializa la serie al cerrarse cada una.
/// </summary>
public sealed record TemplateApplicationResponse(
    Guid TemplateId,
    int TasksCreated,
    int DependenciesCreated,
    Guid FirstTaskId,
    IReadOnlyList<Guid> TaskIds,
    Guid? SeriesId
);
