using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Templates;

/// <summary>
/// Convierte el guion en el grafo: N tareas, sus subtareas y las aristas entre ellas. Cruza tres
/// agregados —plantilla, tarea y dependencia— así que no vive en ninguno. No persiste: muta lo
/// rastreado y guarda el handler, para que las N tareas y las M aristas entren en la misma
/// transacción o no entre ninguna.
/// </summary>
public sealed class TaskTemplateInstantiator(ITaskRepository tasks, ITaskDependencyRepository dependencies)
    : ITaskTemplateInstantiator
{
    public Result<TemplateInstantiation> Instantiate(TaskTemplate template, TemplateApplication application)
    {
        if (!template.IsActive)
            return Result.Failure<TemplateInstantiation>(TaskErrors.Template.Retired);

        var created = new Dictionary<int, TaskItem>();

        // Los padres primero: una subtarea necesita que su padre ya exista para colgarse de él.
        foreach (var step in template.Steps.OrderBy(s => s.ParentStepOrder.HasValue).ThenBy(s => s.Order))
        {
            var task = BuildStep(template, step, application, created);
            if (task.IsFailure)
                return Result.Failure<TemplateInstantiation>(task.Error);

            var reference = AttachTemplateFiles(template, step.Order, task.Value, application);
            if (reference.IsFailure)
                return Result.Failure<TemplateInstantiation>(reference.Error);

            created[step.Order] = task.Value;
        }

        var edges = BuildEdges(template, application, created);
        if (edges.IsFailure)
            return Result.Failure<TemplateInstantiation>(edges.Error);

        foreach (var task in created.Values)
            tasks.Add(task);

        foreach (var edge in edges.Value)
            dependencies.Add(edge);

        return Result.Success(new TemplateInstantiation([.. created.Values], edges.Value));
    }

    /// <summary>
    /// Los archivos de referencia del guion viajan a la instancia con el mismo <c>fileId</c>: el
    /// checklist en PDF se guarda una vez en CloudStorage por muchas veces que se aplique.
    /// </summary>
    private static Result AttachTemplateFiles(
        TaskTemplate template,
        int stepOrder,
        TaskItem task,
        TemplateApplication application
    )
    {
        foreach (var reference in template.AttachmentsFor(stepOrder))
        {
            var attached = task.AttachTemplateFile(
                reference.FileId,
                reference.DisplayName,
                reference.ContentType,
                reference.SizeBytes,
                application.ByUserId,
                application.NowUtc
            );
            if (attached.IsFailure)
                return Result.Failure(attached.Error);
        }

        return Result.Success();
    }

    private static Result<TaskItem> BuildStep(
        TaskTemplate template,
        TaskTemplateStep step,
        TemplateApplication application,
        Dictionary<int, TaskItem> created
    )
    {
        var due = DueDate.Create(
            application.DueAtUtc.AddDays(step.DueOffsetDays),
            application.TimeZoneId,
            step.IsStatutory
        );
        if (due.IsFailure)
            return Result.Failure<TaskItem>(due.Error);

        // Una referencia por tarea, no una compartida: EF Core no admite la misma instancia de un
        // owned type en varios propietarios y deja el cliente en blanco en todas menos una.
        var reference = TaskReference.Create(application.CustomerId, application.TaxYear);
        if (reference.IsFailure)
            return Result.Failure<TaskItem>(reference.Error);

        // Una subtarea hereda la referencia del padre por construcción; una tarea suelta la recibe.
        var result = step.ParentStepOrder is { } parentOrder
            ? TaskItem.CreateSubtask(
                created[parentOrder],
                application.ByUserId,
                step.Title,
                step.Description,
                step.Priority,
                due.Value,
                step.Estimated,
                application.AssigneeUserId,
                application.NowUtc
            )
            : TaskItem.Create(
                template.TenantId,
                application.ByUserId,
                step.Title,
                step.Description,
                step.Priority,
                reference.Value,
                due.Value,
                step.Estimated,
                application.AssigneeUserId,
                application.NowUtc
            );

        if (result.IsSuccess)
            result.Value.AttachToTemplate(template.Id);

        return result;
    }

    /// <summary>
    /// Las aristas se arman después de crear todas las tareas: el paso 2 depende del 1, y hasta que
    /// el 1 no existe no hay id al que apuntar.
    /// </summary>
    private static Result<IReadOnlyList<TaskDependency>> BuildEdges(
        TaskTemplate template,
        TemplateApplication application,
        Dictionary<int, TaskItem> created
    )
    {
        var edges = new List<TaskDependency>();

        foreach (var step in template.Steps.Where(s => s.DependsOnStepOrder is not null))
        {
            var successor = created[step.Order];
            var predecessor = created[step.DependsOnStepOrder!.Value];

            var edge = TaskDependency.Create(
                template.TenantId,
                successor.Id,
                predecessor.Id,
                application.ByUserId,
                application.NowUtc
            );
            if (edge.IsFailure)
                return Result.Failure<IReadOnlyList<TaskDependency>>(edge.Error);

            // El predecesor nace abierto, así que la sucesora nace bloqueada: sólo el paso 1 se puede
            // trabajar el primer día.
            successor.RegisterBlockerAdded();
            edges.Add(edge.Value);
        }

        return Result.Success<IReadOnlyList<TaskDependency>>(edges);
    }
}
