using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>
/// La unidad de trabajo. Se llama <c>TaskItem</c> y no <c>Task</c> para no chocar con
/// <c>System.Threading.Tasks.Task</c>.
/// </summary>
public sealed partial class TaskItem : AggregateRoot, IHasOwner
{
    public TaskTitle Title { get; private set; } = default!;
    public TaskDescription? Description { get; private set; }
    public TaskItemStatus Status { get; private set; }

    /// <summary>Estado de la oficina dentro de la etapa; null = el estado por defecto de la etapa.</summary>
    public Guid? WorkflowStatusId { get; private set; }

    /// <summary>Código de ese estado: no cambia nunca, así que se guarda junto al id.</summary>
    public string? WorkflowStatusCode { get; private set; }
    public TaskPriority Priority { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "Da visibilidad al creador; no se transfiere.")]
    public Guid CreatedByUserId { get; private set; }

    [UserReference(UserReferenceKind.ActiveWork, HandledBy = "TasksUserOffboardedConsumer")]
    public Guid? AssigneeUserId { get; private set; }

    /// <summary>IDs opacos: Task no valida contra Customer.</summary>
    public TaskReference Reference { get; private set; } = TaskReference.None;

    public DueDate? Due { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? CompletedByUserId { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? CancelledByUserId { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>En la papelera desde (D-25). Nulo = viva. La purga la borra de verdad a los 30 días.</summary>
    public DateTime? DeletedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? DeletedByUserId { get; private set; }

    /// <summary>La tarea que se borró: ella misma o la ascendiente con la que se fue. Restore trae la rama entera.</summary>
    public Guid? TrashRootId { get; private set; }

    public bool IsInTrash => DeletedAtUtc is not null;

    /// <summary>Días que pasa una tarea en la papelera antes de borrarse de verdad.</summary>
    public const int TrashRetentionDays = 30;

    /// <summary>
    /// Cuándo se avisó de que venció. El barrido pasa cada hora y la tarea sigue vencida mañana: sin
    /// esta marca, el asignado recibiría el mismo aviso indefinidamente hasta silenciar el canal.
    /// </summary>
    public DateTime? OverdueNotifiedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? ParentTaskId { get; private set; }

    /// <summary>Raíz + 2 niveles de subtareas.</summary>
    public const int MaxDepth = 2;

    public const int MaxDirectChildren = 50;

    /// <summary>Se guarda, no se calcula subiendo el árbol.</summary>
    public int Depth { get; private set; }
    public int OpenSubtaskCount { get; private set; }

    /// <summary>Todas las subtareas directas, abiertas o cerradas: da el "2 of 5" y el tope.</summary>
    public int SubtaskCount { get; private set; }

    public int OpenBlockerCount { get; private set; }

    /// <summary>Derivado del contador, no una columna de estado.</summary>
    public bool IsBlocked => OpenBlockerCount > 0;

    /// <summary>Veces reabierta: da la clave del recordatorio por ciclo de vida.</summary>
    public int ReopenCount { get; private set; }

    public Guid? SeriesId { get; private set; }
    public int? OccurrenceNumber { get; private set; }

    /// <summary>
    /// De qué plantilla nació. Sin esto no hay forma de saber si el 1040 de este cliente y año ya se
    /// instanció: mirar los títulos sería adivinar, y el preparador acabaría con el encargo duplicado.
    /// </summary>
    public Guid? TemplateId { get; private set; }

    public EstimatedHours? Estimated { get; private set; }

    /// <summary>Suma de los timers ya cerrados. Se acumula al parar, no se recalcula al leer.</summary>
    public decimal ActualHours { get; private set; }

    private readonly List<TaskTimer> _timers = [];
    public IReadOnlyList<TaskTimer> Timers => _timers;

    private readonly List<TaskAttachment> _attachments = [];
    public IReadOnlyList<TaskAttachment> Attachments => _attachments;

    public const int MaxActiveAttachments = 20;

    public ClientRequestNote? ExpectedItems { get; private set; }
    public DateTime? ClientDueAtUtc { get; private set; }

    /// <summary>La fecha civil pedida al cliente; su instante (fin del día de la oficina) ya no la dice.</summary>
    public DateOnly? ClientDueOn { get; private set; }

    public DateOnly? ClientDueCivilDate => ClientDueOn ?? DueDate.LegacyCivilDate(ClientDueAtUtc);

    [UserReference(
        UserReferenceKind.Provenance,
        Note = "Quién pidió al cliente; el aviso va por ClientRequest.FollowUpUserId."
    )]
    public Guid? ClientRequestedByUserId { get; private set; }
    public DateTime? ClientRequestedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    private TaskItem() { }

    public static Result<TaskItem> Create(
        Guid tenantId,
        Guid createdByUserId,
        TaskTitle title,
        TaskDescription? description,
        TaskPriority priority,
        TaskReference reference,
        DueDate? due,
        EstimatedHours? estimated,
        Guid? assigneeUserId,
        DateTime nowUtc,
        bool recurringOccurrence = false
    )
    {
        if (tenantId == Guid.Empty || createdByUserId == Guid.Empty)
            return Result.Failure<TaskItem>(TaskErrors.OwnerRequired);

        if (assigneeUserId == Guid.Empty)
            return Result.Failure<TaskItem>(TaskErrors.AssigneeRequired);

        var task = new TaskItem
        {
            Title = title,
            Description = description,
            Status = TaskItemStatus.NotStarted,
            Priority = priority,
            CreatedByUserId = createdByUserId,
            AssigneeUserId = assigneeUserId,
            // Copia propia: la del padre (subtarea) o TaskReference.None no pueden compartirse entre filas.
            Reference = reference.CopyForNewRow(),
            Due = due,
            Estimated = estimated,
            CreatedAtUtc = nowUtc,
        };
        task.SetTenant(tenantId);

        task.AddDomainEvent(
            new TaskCreatedDomainEvent(
                task.Id,
                tenantId,
                createdByUserId,
                assigneeUserId,
                null,
                nowUtc,
                recurringOccurrence
            )
        );
        return Result.Success(task);
    }

    /// <summary>
    /// El padre entra entero, no sólo su id: las tres guardas leen su estado y su profundidad, y
    /// pedirlas sueltas deja que el llamador las invente.
    /// </summary>
    public static Result<TaskItem> CreateSubtask(
        TaskItem parent,
        Guid createdByUserId,
        TaskTitle title,
        TaskDescription? description,
        TaskPriority priority,
        DueDate? due,
        EstimatedHours? estimated,
        Guid? assigneeUserId,
        DateTime nowUtc
    )
    {
        if (parent.Depth >= MaxDepth)
            return Result.Failure<TaskItem>(TaskErrors.MaxDepthExceeded(MaxDepth));

        if (parent.SubtaskCount >= MaxDirectChildren)
            return Result.Failure<TaskItem>(TaskErrors.TooManyChildren(MaxDirectChildren));

        if (parent.IsClosed)
            return Result.Failure<TaskItem>(TaskErrors.CannotAddSubtaskToClosedParent);

        var result = Create(
            parent.TenantId,
            createdByUserId,
            title,
            description,
            priority,
            parent.Reference,
            due,
            estimated,
            assigneeUserId,
            nowUtc
        );
        if (result.IsFailure)
            return result;

        var subtask = result.Value;
        subtask.ParentTaskId = parent.Id;
        subtask.Depth = parent.Depth + 1;
        parent.RegisterSubtaskOpened();
        parent.SubtaskCount++;

        return Result.Success(subtask);
    }

    public Result Start(Guid byUserId, DateTime nowUtc)
    {
        if (IsClosed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(Start)));

        // Ya en curso: no-op antes de mirar el bloqueo (un bloqueo nuevo no la saca de In Progress).
        if (Status == TaskItemStatus.InProgress)
            return Result.Success();

        if (IsBlocked)
            return Result.Failure(TaskErrors.BlockedByDependencies(OpenBlockerCount));

        // Sólo la primera vez: Reopen() deriva su estado destino de este campo.
        StartedAtUtc ??= nowUtc;
        ChangeCategory(TaskItemStatus.InProgress);

        AddDomainEvent(new TaskStartedDomainEvent(Id, TenantId, byUserId, nowUtc));
        return Result.Success();
    }

    /// <summary>
    /// Volver a Not Started desde el tablero (In Progress o Waiting on Client). Una tarea cerrada se reabre primero.
    /// Olvida el inicio: si después se completa y se reabre, <see cref="Reopen"/> la devuelve a Not Started.
    /// </summary>
    public Result MoveToNotStarted(Guid byUserId, DateTime nowUtc)
    {
        if (IsClosed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(MoveToNotStarted)));

        if (Status == TaskItemStatus.NotStarted)
            return Result.Success();

        var previous = Status;
        ChangeCategory(TaskItemStatus.NotStarted);
        StartedAtUtc = null;

        AddDomainEvent(new TaskMovedToNotStartedDomainEvent(Id, TenantId, previous, byUserId, nowUtc));
        return Result.Success();
    }

    /// <summary>No exige <see cref="TaskItemStatus.InProgress"/>: una tarea corta se crea y se cierra.</summary>
    public Result Complete(Guid byUserId, DateTime nowUtc)
    {
        if (Status == TaskItemStatus.Completed)
            return Result.Success();

        if (Status == TaskItemStatus.Cancelled)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(Complete)));

        if (IsBlocked)
            return Result.Failure(TaskErrors.BlockedByDependencies(OpenBlockerCount));

        if (OpenSubtaskCount > 0)
            return Result.Failure(TaskErrors.HasOpenSubtasks(OpenSubtaskCount));

        ChangeCategory(TaskItemStatus.Completed);
        CompletedAtUtc = nowUtc;
        CompletedByUserId = byUserId;

        AddDomainEvent(new TaskCompletedDomainEvent(Id, TenantId, ParentTaskId, SeriesId, byUserId, nowUtc));
        return Result.Success();
    }

    /// <summary>
    /// El estado destino se deriva de <see cref="StartedAtUtc"/>, sin columna <c>PreviousStatus</c>.
    /// Nunca vuelve a <see cref="TaskItemStatus.WaitingOnClient"/>: la petición anterior ya no vale y
    /// la tarea quedaría esperando algo que nadie pidió.
    /// </summary>
    public Result Reopen(Guid byUserId, DateTime nowUtc)
    {
        if (!IsClosed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(Reopen)));

        ChangeCategory(StartedAtUtc is null ? TaskItemStatus.NotStarted : TaskItemStatus.InProgress);
        CompletedAtUtc = null;
        CompletedByUserId = null;
        CancelledAtUtc = null;
        CancelledByUserId = null;
        CancellationReason = null;
        ReopenCount++;

        AddDomainEvent(new TaskReopenedDomainEvent(Id, TenantId, ParentTaskId, Status, byUserId, nowUtc, ReopenCount));
        return Result.Success();
    }

    public Result Cancel(string? reason, Guid byUserId, DateTime nowUtc)
    {
        if (Status == TaskItemStatus.Cancelled)
            return Result.Success();

        if (Status == TaskItemStatus.Completed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(Cancel)));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(TaskErrors.CancellationReasonRequired);
        if (reason.Trim().Length > TaskErrors.CancellationReasonMaxLength)
            return Result.Failure(TaskErrors.CancellationReasonTooLong);

        ChangeCategory(TaskItemStatus.Cancelled);
        CancelledAtUtc = nowUtc;
        CancelledByUserId = byUserId;
        CancellationReason = reason.Trim();

        AddDomainEvent(
            new TaskCancelledDomainEvent(Id, TenantId, ParentTaskId, SeriesId, reason.Trim(), byUserId, nowUtc)
        );
        return Result.Success();
    }

    /// <summary>
    /// Sin restricción de dirección: un empleado puede asignarle a un admin. El flujo de revisión
    /// interna lo exige y el contrapeso es <see cref="Unassign"/>, no un 403.
    /// </summary>
    public Result Assign(Guid assigneeUserId, Guid byUserId, DateTime nowUtc)
    {
        if (assigneeUserId == Guid.Empty)
            return Result.Failure(TaskErrors.AssigneeRequired);

        if (IsClosed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(Assign)));

        if (AssigneeUserId == assigneeUserId)
            return Result.Success();

        var previousAssigneeUserId = AssigneeUserId;
        AssigneeUserId = assigneeUserId;

        AddDomainEvent(
            new TaskAssignedDomainEvent(
                Id,
                TenantId,
                assigneeUserId,
                previousAssigneeUserId,
                byUserId,
                nowUtc,
                ReopenCount
            )
        );
        return Result.Success();
    }

    public Result Unassign(Guid byUserId, DateTime nowUtc)
    {
        if (IsClosed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(Unassign)));

        if (AssigneeUserId is not { } previousAssigneeUserId)
            return Result.Success();

        AssigneeUserId = null;

        AddDomainEvent(new TaskUnassignedDomainEvent(Id, TenantId, previousAssigneeUserId, byUserId, nowUtc));
        return Result.Success();
    }

    /// <summary>Aflojar un vencimiento estatutario exige razón; adelantarlo y los internos no.</summary>
    public Result ChangeDue(DueDate? due, Guid byUserId, DateTime nowUtc, string? statutoryChangeReason = null)
    {
        if (IsClosed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(ChangeDue)));

        var reason = statutoryChangeReason?.Trim();
        if (RelaxesStatutoryDue(due))
        {
            if (string.IsNullOrEmpty(reason))
                return Result.Failure(TaskErrors.Due.StatutoryReasonRequired);
            if (reason.Length > TaskErrors.StatutoryChangeReasonMaxLength)
                return Result.Failure(TaskErrors.Due.StatutoryReasonTooLong);
        }
        else
        {
            // Sin razón que guardar el audit distingue de un vistazo qué movidas importan.
            reason = null;
        }

        var previousDueAtUtc = Due?.DueAtUtc;
        Due = due;

        // Fecha nueva, aviso nuevo: si vuelve a vencer, vuelve a avisarse.
        OverdueNotifiedAtUtc = null;

        AddDomainEvent(
            new TaskDueChangedDomainEvent(
                Id,
                TenantId,
                previousDueAtUtc,
                due?.DueAtUtc,
                due?.TimeZoneId,
                byUserId,
                nowUtc,
                reason
            )
        );
        return Result.Success();
    }

    // Quitarle la marca cuenta como aflojar: sin eso bastaría desmarcar y mover libre después.
    private bool RelaxesStatutoryDue(DueDate? due)
    {
        if (Due is not { IsStatutory: true } current)
            return false;

        return due is null || due.DueAtUtc > current.DueAtUtc || !due.IsStatutory;
    }

    public Result ChangePriority(TaskPriority priority, Guid byUserId, DateTime nowUtc)
    {
        if (IsClosed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(ChangePriority)));

        if (Priority == priority)
            return Result.Success();

        var previousPriority = Priority;
        Priority = priority;

        AddDomainEvent(new TaskPriorityChangedDomainEvent(Id, TenantId, previousPriority, priority, byUserId, nowUtc));
        return Result.Success();
    }

    /// <summary>Sin evento: nadie fuera de la tarea reacciona a un cambio de título.</summary>
    public Result ChangeTitle(TaskTitle title)
    {
        if (IsClosed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(ChangeTitle)));

        Title = title;
        return Result.Success();
    }

    public Result ChangeDescription(TaskDescription? description)
    {
        if (IsClosed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(ChangeDescription)));

        Description = description;
        return Result.Success();
    }

    /// <summary>
    /// <paramref name="expectedItems"/> es obligatorio: viaja hasta el correo al cliente.
    /// <paramref name="clientDueAtUtc"/> es la fecha que se le pide al cliente, distinta del
    /// vencimiento de la tarea. Quién pidió y cuándo se persisten porque la respuesta del cliente
    /// (lo que sube semanas después) se atribuye a ese pedido.
    /// </summary>
    public Result MoveToWaitingOnClient(
        ClientRequestNote expectedItems,
        DateTime? clientDueAtUtc,
        Guid byUserId,
        DateTime nowUtc,
        DateOnly? clientDueOn = null
    )
    {
        if (IsClosed)
            return Result.Failure(TaskErrors.WaitingOnClient.TaskClosed);

        if (clientDueAtUtc is { Kind: not DateTimeKind.Utc })
            return Result.Failure(TaskErrors.WaitingOnClient.ClientDueNotUtc);

        ChangeCategory(TaskItemStatus.WaitingOnClient);
        ExpectedItems = expectedItems;
        ClientDueAtUtc = clientDueAtUtc;
        ClientDueOn = clientDueAtUtc is null ? null : clientDueOn;
        ClientRequestedByUserId = byUserId;
        ClientRequestedAtUtc = nowUtc;

        AddDomainEvent(
            new TaskMovedToWaitingOnClientDomainEvent(
                Id,
                TenantId,
                Reference.CustomerId,
                Reference.TaxYear,
                expectedItems.Value,
                clientDueAtUtc,
                byUserId,
                nowUtc
            )
        );
        return Result.Success();
    }

    // internal: los ve Application (InternalsVisibleTo) pero no Api, así que un controller que
    // intente llamarlos no compila.

    /// <summary>
    /// Marca la tarea como ocurrencia de una serie. Se hace después de crearla y no dentro de
    /// <see cref="Create"/>: la serie necesita el id de la tarea para apuntar a su instancia abierta, y
    /// la tarea necesita existir para tenerlo.
    /// </summary>
    internal void AttachToSeries(Guid seriesId, int occurrenceNumber)
    {
        SeriesId = seriesId;
        OccurrenceNumber = occurrenceNumber;
    }

    internal void AttachToTemplate(Guid templateId) => TemplateId = templateId;

    private bool IsClosed => TaskItemStatuses.IsClosed(Status);

    /// <summary>
    /// P9: la oficina cambió de zona. El vencimiento propio y el del cliente que eran fin de día en la zona anterior
    /// pasan al fin del mismo día en la nueva. No es un cambio de fecha: no avisa ni reinicia el aviso de vencida.
    /// </summary>
    public bool RezoneCivilDues(string previousTimeZoneId, ResolvedTimeZone newZone)
    {
        if (IsClosed)
            return false;

        var changed = false;
        if (Due is { } due && due.TimeZoneId == previousTimeZoneId && due.IsEndOfCivilDay)
        {
            Due = DueDate.EndOfCivilDay(due.DueOn, newZone, due.IsStatutory).Value;
            changed = true;
        }

        var previousZone = ResolvedTimeZone.Resolve(previousTimeZoneId);
        if (ClientDueOn is { } clientDay && ClientDueAtUtc == LocalDay.EndUtc(clientDay, previousZone))
        {
            ClientDueAtUtc = LocalDay.EndUtc(clientDay, newZone);
            changed = true;
        }
        return changed;
    }
}
