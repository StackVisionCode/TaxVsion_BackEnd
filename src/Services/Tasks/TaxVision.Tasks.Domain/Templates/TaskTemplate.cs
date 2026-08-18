using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Templates;

/// <summary>
/// El guion de un encargo fiscal: «un 1040 son estos seis pasos, en este orden, con estas fechas
/// relativas al 15 de abril». Aplicarla instancia el grafo entero; la plantilla no sabe de instancias
/// ni las cuenta.
/// </summary>
public sealed class TaskTemplate : BaseEntity, ITenantOwned
{
    private readonly List<TaskTemplateStep> _steps = [];

    private TaskTemplate() { }

    public Guid TenantId { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Activa o retirada. No se borra: hay tareas vivas que nacieron de ella.</summary>
    public bool IsActive { get; private set; }

    public IReadOnlyList<TaskTemplateStep> Steps => _steps.AsReadOnly();

    private readonly List<TaskTemplateAttachment> _attachments = [];

    /// <summary>Los archivos de referencia del guion, compartidos por todas sus instancias.</summary>
    public IReadOnlyList<TaskTemplateAttachment> Attachments => _attachments.AsReadOnly();

    /// <summary>
    /// Una 1040-ES no es un grafo de seis pasos: es el mismo encargo cuatro veces al año. Cuando la
    /// plantilla trae regla, aplicarla abre una serie con su único paso de blueprint en vez de
    /// instanciar tareas sueltas, y las fechas las pone la regla, no el offset.
    /// </summary>
    public RecurrenceRule? Recurrence { get; private set; }

    public RecurrenceMode RecurrenceMode { get; private set; }

    public bool IsRecurring => Recurrence is not null;

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public const int MaxSteps = 50;

    public const int MaxAttachments = 20;

    public static Result<TaskTemplate> Create(
        Guid tenantId,
        Guid createdByUserId,
        string? name,
        string? description,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty || createdByUserId == Guid.Empty)
            return Result.Failure<TaskTemplate>(TaskErrors.OwnerRequired);

        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return Result.Failure<TaskTemplate>(TaskErrors.Template.NameRequired);

        if (trimmed.Length > 200)
            return Result.Failure<TaskTemplate>(TaskErrors.Template.NameTooLong);

        return Result.Success(
            new TaskTemplate
            {
                TenantId = tenantId,
                CreatedByUserId = createdByUserId,
                Name = trimmed,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                IsActive = true,
                CreatedAtUtc = nowUtc,
            }
        );
    }

    /// <summary>
    /// Reemplaza el guion completo en vez de mutar paso a paso: las referencias son por
    /// <c>Order</c>, así que quitar un paso del medio invalida las de los demás. Validar el conjunto
    /// entero de una vez es la única forma de no dejar la plantilla a medio arreglar.
    /// </summary>
    public Result ReplaceSteps(IReadOnlyList<TaskTemplateStep> steps, DateTime nowUtc)
    {
        if (steps.Count == 0)
            return Result.Failure(TaskErrors.Template.StepsRequired);

        if (IsRecurring && steps.Count != 1)
            return Result.Failure(TaskErrors.Template.RecurringNeedsSingleStep);

        if (steps.Count > MaxSteps)
            return Result.Failure(TaskErrors.Template.TooManySteps);

        var orders = steps.Select(s => s.Order).ToHashSet();
        if (orders.Count != steps.Count)
            return Result.Failure(TaskErrors.Template.DuplicateStepOrder);

        foreach (var step in steps)
        {
            if (step.DependsOnStepOrder is { } dependsOn && !orders.Contains(dependsOn))
                return Result.Failure(TaskErrors.Template.StepReferenceMissing);

            if (step.ParentStepOrder is { } parent && !orders.Contains(parent))
                return Result.Failure(TaskErrors.Template.StepReferenceMissing);
        }

        var dependencyCycle = EnsureNoCycle(steps, s => s.DependsOnStepOrder);
        if (dependencyCycle.IsFailure)
            return dependencyCycle;

        var parentCycle = EnsureNoCycle(steps, s => s.ParentStepOrder);
        if (parentCycle.IsFailure)
            return Result.Failure(TaskErrors.Template.ParentCycle);

        _steps.Clear();
        foreach (var step in steps.OrderBy(s => s.Order))
        {
            step.AttachTo(Id);
            _steps.Add(step);
        }

        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Cada paso tiene a lo sumo un predecesor, así que el grafo es una cadena por rama: basta con
    /// seguir los punteros contando saltos. No hace falta el recorrido general de
    /// <c>TaskDependencyGraph</c>, que resuelve el caso de varios predecesores por nodo.
    /// </summary>
    private static Result EnsureNoCycle(IReadOnlyList<TaskTemplateStep> steps, Func<TaskTemplateStep, int?> next)
    {
        var byOrder = steps.ToDictionary(s => s.Order);

        foreach (var start in steps)
        {
            var current = next(start);
            var hops = 0;

            while (current is { } order && byOrder.TryGetValue(order, out var step))
            {
                if (order == start.Order || ++hops > steps.Count)
                    return Result.Failure(TaskErrors.Template.StepCycle);

                current = next(step);
            }
        }

        return Result.Success();
    }

    public Result Rename(string? name, string? description, DateTime nowUtc)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return Result.Failure(TaskErrors.Template.NameRequired);

        if (trimmed.Length > 200)
            return Result.Failure(TaskErrors.Template.NameTooLong);

        Name = trimmed;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// La regla se fija con el guion, no después: cambiar a recurrente una plantilla de seis pasos
    /// dejaría cinco huérfanos, y quitarle la regla a una serie dejaría un paso sin vencimiento.
    /// </summary>
    public Result SetRecurrence(RecurrenceRule? rule, RecurrenceMode mode, DateTime nowUtc)
    {
        if (rule is not null && _steps.Count > 1)
            return Result.Failure(TaskErrors.Template.RecurringNeedsSingleStep);

        Recurrence = rule;
        RecurrenceMode = mode;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Reemplaza los archivos de referencia. El <c>stepOrder</c> nulo cuelga el archivo del primer
    /// paso; uno que no existe se rechaza antes de guardar, no al aplicar la plantilla.
    /// </summary>
    public Result ReplaceAttachments(IReadOnlyList<TaskTemplateAttachment> attachments, DateTime nowUtc)
    {
        if (attachments.Count > MaxAttachments)
            return Result.Failure(TaskErrors.Template.TooManyAttachments);

        var orders = _steps.Select(s => s.Order).ToHashSet();
        if (attachments.Any(a => a.StepOrder is { } order && !orders.Contains(order)))
            return Result.Failure(TaskErrors.Template.StepReferenceMissing);

        if (attachments.Select(a => a.FileId).Distinct().Count() != attachments.Count)
            return Result.Failure(TaskErrors.Template.DuplicateAttachment);

        _attachments.Clear();
        foreach (var attachment in attachments)
        {
            attachment.AttachTo(Id);
            _attachments.Add(attachment);
        }

        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Los que le tocan a un paso: los suyos, más los que no eligieron paso si es el primero.</summary>
    public IEnumerable<TaskTemplateAttachment> AttachmentsFor(int stepOrder)
    {
        var firstOrder = _steps.Count == 0 ? 0 : _steps.Min(s => s.Order);

        return _attachments.Where(a => a.StepOrder == stepOrder || (a.StepOrder is null && stepOrder == firstOrder));
    }

    public void Retire(DateTime nowUtc)
    {
        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    public void Reactivate(DateTime nowUtc)
    {
        IsActive = true;
        UpdatedAtUtc = nowUtc;
    }
}
