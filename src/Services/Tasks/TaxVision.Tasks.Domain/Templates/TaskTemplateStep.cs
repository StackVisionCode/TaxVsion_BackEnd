using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Templates;

/// <summary>
/// Un paso del guion, no una tarea. Las referencias entre pasos van por <see cref="Order"/> y no por
/// id porque la plantilla se edita antes de que exista ninguna instancia: quien la arma piensa en
/// «el paso 3 va después del 2», y un id todavía no significa nada para él.
/// </summary>
public sealed class TaskTemplateStep : BaseEntity
{
    private TaskTemplateStep() { }

    public Guid TaskTemplateId { get; private set; }

    /// <summary>Posición en el guion, 1..N. Es la identidad que usan las referencias entre pasos.</summary>
    public int Order { get; private set; }

    public TaskTitle Title { get; private set; } = default!;
    public TaskDescription? Description { get; private set; }
    public TaskPriority Priority { get; private set; }
    public EstimatedHours? Estimated { get; private set; }

    /// <summary>
    /// Días respecto del vencimiento que se pasa al aplicar la plantilla. Negativo = antes: el paso
    /// «solicitar documentos» de un 1040 vence 45 días antes del 15 de abril, no después.
    /// </summary>
    public int DueOffsetDays { get; private set; }

    public bool IsStatutory { get; private set; }

    /// <summary>El paso que debe cerrarse antes que éste. Nulo en el primero de cada rama.</summary>
    public int? DependsOnStepOrder { get; private set; }

    /// <summary>Si está, este paso nace como subtarea de aquél en vez de como tarea suelta.</summary>
    public int? ParentStepOrder { get; private set; }

    /// <summary>
    /// Pista para quien asigne, no una asignación. Task no resuelve roles —no tiene la proyección— y
    /// fingir que sí dejaría tareas asignadas a un rol que el tenant renombró hace un año.
    /// </summary>
    public string? SuggestedRoleName { get; private set; }

    internal static Result<TaskTemplateStep> Create(
        int order,
        TaskTitle title,
        TaskDescription? description,
        TaskPriority priority,
        EstimatedHours? estimated,
        int dueOffsetDays,
        bool isStatutory,
        int? dependsOnStepOrder,
        int? parentStepOrder,
        string? suggestedRoleName
    )
    {
        if (order <= 0)
            return Result.Failure<TaskTemplateStep>(TaskErrors.Template.StepOrderInvalid);

        if (dependsOnStepOrder == order || parentStepOrder == order)
            return Result.Failure<TaskTemplateStep>(TaskErrors.Template.StepSelfReference);

        return Result.Success(
            new TaskTemplateStep
            {
                Order = order,
                Title = title,
                Description = description,
                Priority = priority,
                Estimated = estimated,
                DueOffsetDays = dueOffsetDays,
                IsStatutory = isStatutory,
                DependsOnStepOrder = dependsOnStepOrder,
                ParentStepOrder = parentStepOrder,
                SuggestedRoleName = string.IsNullOrWhiteSpace(suggestedRoleName) ? null : suggestedRoleName.Trim(),
            }
        );
    }

    internal void AttachTo(Guid templateId) => TaskTemplateId = templateId;
}
