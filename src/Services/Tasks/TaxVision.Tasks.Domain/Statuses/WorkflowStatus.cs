using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Statuses;

/// <summary>
/// Estado de la oficina dentro de una categoría (<see cref="TaskItemStatus"/>). El motor lee siempre la
/// categoría; el nombre, el color y el orden son de la oficina y no cambian ninguna regla.
/// Evoluciona a la antigua label: misma fila, misma tabla renombrada.
/// </summary>
/// <remarks>
/// Raíz pequeña y propia: la tarea la referencia por id (<see cref="WorkflowStatusRef"/>), no la contiene.
/// </remarks>
public sealed class WorkflowStatus : TenantEntity
{
    public TaskLabelCode Code { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public LabelColor Color { get; private set; } = default!;

    /// <summary>La etapa que entiende el motor. Antes <c>MapsToStatus</c>.</summary>
    public TaskItemStatus Category { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>El que recibe una tarea que llega a la categoría por un verbo, una serie o una plantilla.</summary>
    public bool IsDefault { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }
    public string? Description { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? CreatedByUserId { get; private set; }
    public DateTime? CreatedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? UpdatedByUserId { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public const int DisplayNameMaxLength = 60;
    public const int DescriptionMaxLength = 200;

    /// <summary>Tope técnico de estados activos por oficina.</summary>
    public const int MaxActivePerTenant = 30;

    /// <summary>Lleva un código reservado: personaliza el estado del sistema de su categoría.</summary>
    public bool IsSystem => TaskLabelCode.IsReserved(Code);

    private WorkflowStatus() { }

    /// <summary>Alta de las labels antiguas (<c>POST /tasks/labels</c>): código del cliente y cualquier categoría.</summary>
    public static Result<WorkflowStatus> Create(
        Guid tenantId,
        TaskLabelCode code,
        string? displayName,
        LabelColor color,
        TaskItemStatus category,
        int sortOrder
    )
    {
        var name = Normalize(displayName);
        if (name.IsFailure)
            return Result.Failure<WorkflowStatus>(name.Error);

        if (!Enum.IsDefined(category))
            return Result.Failure<WorkflowStatus>(TaskErrors.Label.StatusInvalid);

        var status = new WorkflowStatus
        {
            Code = code,
            DisplayName = name.Value,
            Color = color,
            Category = category,
            SortOrder = sortOrder,
        };
        status.SetTenant(tenantId);
        return Result.Success(status);
    }

    /// <summary>Un estado nuevo de la oficina: solo en categorías abiertas y con código generado.</summary>
    public static Result<WorkflowStatus> CreateCustom(
        Guid tenantId,
        TaskLabelCode code,
        string? displayName,
        LabelColor color,
        TaskItemStatus category,
        int sortOrder,
        string? description,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        if (!IsOpenCategory(category) || TaskLabelCode.IsReserved(code))
            return Result.Failure<WorkflowStatus>(TaskErrors.Status.CategoryLocked);

        var created = Create(tenantId, code, displayName, color, category, sortOrder);
        if (created.IsFailure)
            return created;

        var described = created.Value.Describe(description, byUserId, nowUtc);
        if (described.IsFailure)
            return Result.Failure<WorkflowStatus>(described.Error);

        created.Value.CreatedByUserId = byUserId;
        created.Value.CreatedAtUtc = nowUtc;
        return created;
    }

    /// <summary>
    /// Nombre y color propios para el estado del sistema de una categoría (el único posible en Completed y
    /// Cancelled, D-2). Es el por defecto salvo que la oficina ya haya elegido otro.
    /// </summary>
    public static Result<WorkflowStatus> CustomizeSystem(
        Guid tenantId,
        TaskItemStatus category,
        string? displayName,
        LabelColor color,
        bool isDefault,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var created = Create(tenantId, TaskLabelCode.SystemCodeOf(category), displayName, color, category, 0);
        if (created.IsFailure)
            return created;

        created.Value.IsDefault = isDefault;
        created.Value.CreatedByUserId = byUserId;
        created.Value.CreatedAtUtc = nowUtc;
        return created;
    }

    public static bool IsOpenCategory(TaskItemStatus category) =>
        category is TaskItemStatus.NotStarted or TaskItemStatus.InProgress or TaskItemStatus.WaitingOnClient;

    /// <summary>Edición de las labels antiguas. El <see cref="Code"/> no cambia: es lo que el front guarda.</summary>
    public Result Rename(string? displayName, LabelColor color, TaskItemStatus category, int sortOrder)
    {
        var name = Normalize(displayName);
        if (name.IsFailure)
            return Result.Failure(name.Error);

        // El JSON admite números para el enum: un valor fuera de los cinco estados no se guarda.
        if (!Enum.IsDefined(category))
            return Result.Failure(TaskErrors.Label.StatusInvalid);

        DisplayName = name.Value;
        Color = color;
        Category = category;
        SortOrder = sortOrder;
        return Result.Success();
    }

    public Result Rename(string? displayName, Guid byUserId, DateTime nowUtc)
    {
        var name = Normalize(displayName);
        if (name.IsFailure)
            return Result.Failure(name.Error);

        DisplayName = name.Value;
        Touch(byUserId, nowUtc);
        return Result.Success();
    }

    public void ChangeColor(LabelColor color, Guid byUserId, DateTime nowUtc)
    {
        Color = color;
        Touch(byUserId, nowUtc);
    }

    public Result Describe(string? description, Guid byUserId, DateTime nowUtc)
    {
        var trimmed = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmed?.Length > DescriptionMaxLength)
            return Result.Failure(TaskErrors.Status.DescriptionTooLong);

        Description = trimmed;
        Touch(byUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Posición dentro de su categoría; el orden entre categorías es fijo.</summary>
    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    public Result MakeDefault()
    {
        if (IsArchived)
            return Result.Failure(TaskErrors.Status.Archived);

        IsDefault = true;
        return Result.Success();
    }

    public void ClearDefault() => IsDefault = false;

    /// <summary>Sus tareas abiertas ya se movieron; las cerradas lo conservan como dato histórico.</summary>
    public Result Archive(Guid byUserId, DateTime nowUtc)
    {
        if (IsSystem)
            return Result.Failure(TaskErrors.Status.SystemStatus);
        if (IsArchived)
            return Result.Success();

        IsArchived = true;
        IsDefault = false;
        ArchivedAtUtc = nowUtc;
        Touch(byUserId, nowUtc);
        return Result.Success();
    }

    public void Restore(Guid byUserId, DateTime nowUtc)
    {
        if (!IsArchived)
            return;

        IsArchived = false;
        ArchivedAtUtc = null;
        Touch(byUserId, nowUtc);
    }

    public WorkflowStatusRef ToRef() => new(Id, Code.Value, Category);

    private void Touch(Guid byUserId, DateTime nowUtc)
    {
        UpdatedByUserId = byUserId;
        UpdatedAtUtc = nowUtc;
    }

    private static Result<string> Normalize(string? displayName)
    {
        var trimmed = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Failure<string>(TaskErrors.Label.DisplayNameEmpty);

        return trimmed.Length > DisplayNameMaxLength
            ? Result.Failure<string>(TaskErrors.Label.DisplayNameTooLong)
            : Result.Success(trimmed);
    }
}

/// <summary>
/// Lo que la tarea recibe de un estado ya validado (misma oficina, activo). Sin id ni código: el estado
/// por defecto de la categoría.
/// </summary>
public sealed record WorkflowStatusRef(Guid? Id, string? Code, TaskItemStatus Category)
{
    public static WorkflowStatusRef DefaultOf(TaskItemStatus category) => new(null, null, category);
}
