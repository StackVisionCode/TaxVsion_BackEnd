using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>
/// La unidad de trabajo. Se llama <c>TaskItem</c> y no <c>Task</c> para no chocar con
/// <c>System.Threading.Tasks.Task</c>.
/// </summary>
public sealed class TaskItem : AggregateRoot, IHasOwner
{
    public TaskTitle Title { get; private set; } = default!;
    public TaskDescription? Description { get; private set; }
    public TaskItemStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public Guid? AssigneeUserId { get; private set; }

    /// <summary>IDs opacos: Task no valida contra Customer.</summary>
    public TaskReference Reference { get; private set; } = TaskReference.None;

    public DueDate? Due { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

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

    public int OpenBlockerCount { get; private set; }

    /// <summary>Derivado del contador, no una columna de estado.</summary>
    public bool IsBlocked => OpenBlockerCount > 0;

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
        DateTime nowUtc
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
            Reference = reference,
            Due = due,
            Estimated = estimated,
            CreatedAtUtc = nowUtc,
        };
        task.SetTenant(tenantId);

        task.AddDomainEvent(
            new TaskCreatedDomainEvent(task.Id, tenantId, createdByUserId, assigneeUserId, null, nowUtc)
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

        if (parent.OpenSubtaskCount >= MaxDirectChildren)
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

        return Result.Success(subtask);
    }

    /// <summary>Los descendientes los borra el servicio de jerarquía; esto sólo marca la raíz.</summary>
    public Result Delete(Guid byUserId, DateTime nowUtc)
    {
        AddDomainEvent(new TaskDeletedDomainEvent(Id, TenantId, ParentTaskId, IsClosed, byUserId, nowUtc));
        return Result.Success();
    }

    public Result Start(Guid byUserId, DateTime nowUtc)
    {
        if (IsClosed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(Start)));

        if (IsBlocked)
            return Result.Failure(TaskErrors.BlockedByDependencies(OpenBlockerCount));

        if (Status == TaskItemStatus.InProgress)
            return Result.Success();

        // Sólo la primera vez: Reopen() deriva su estado destino de este campo.
        StartedAtUtc ??= nowUtc;
        Status = TaskItemStatus.InProgress;

        AddDomainEvent(new TaskStartedDomainEvent(Id, TenantId, byUserId, nowUtc));
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

        Status = TaskItemStatus.Completed;
        CompletedAtUtc = nowUtc;

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

        Status = StartedAtUtc is null ? TaskItemStatus.NotStarted : TaskItemStatus.InProgress;
        CompletedAtUtc = null;

        AddDomainEvent(new TaskReopenedDomainEvent(Id, TenantId, ParentTaskId, Status, byUserId, nowUtc));
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

        Status = TaskItemStatus.Cancelled;

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
            new TaskAssignedDomainEvent(Id, TenantId, assigneeUserId, previousAssigneeUserId, byUserId, nowUtc)
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
    /// vencimiento de la tarea. Quién pidió y cuándo se persisten porque
    /// <c>task.client_responded.v1</c> los necesita semanas después.
    /// </summary>
    public Result MoveToWaitingOnClient(
        ClientRequestNote expectedItems,
        DateTime? clientDueAtUtc,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        if (IsClosed)
            return Result.Failure(TaskErrors.WaitingOnClient.TaskClosed);

        if (clientDueAtUtc is { Kind: not DateTimeKind.Utc })
            return Result.Failure(TaskErrors.WaitingOnClient.ClientDueNotUtc);

        Status = TaskItemStatus.WaitingOnClient;
        ExpectedItems = expectedItems;
        ClientDueAtUtc = clientDueAtUtc;
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

    /// <summary>
    /// El único camino que abre un timer. Ni <see cref="Create"/>, ni <see cref="Assign"/>, ni
    /// <see cref="Complete"/>, ni ningún consumer lo llaman: las horas facturables las decide una
    /// persona, no el sistema.
    /// </summary>
    public Result<TaskTimer> StartTimer(Guid userId, bool isBillable, DateTime nowUtc)
    {
        if (IsClosed)
            return Result.Failure<TaskTimer>(TaskErrors.InvalidTransition(Status, nameof(StartTimer)));

        foreach (var running in _timers)
        {
            if (running.IsRunning && running.UserId == userId)
                return Result.Failure<TaskTimer>(TaskErrors.Timer.AlreadyRunning);
        }

        var timer = TaskTimer.Start(Id, userId, isBillable, nowUtc);
        _timers.Add(timer);
        return Result.Success(timer);
    }

    /// <summary>
    /// Sólo lo para quien lo abrió. Se puede parar sobre una tarea ya cerrada: si no, el tramo
    /// quedaría corriendo para siempre y las horas imputadas nunca cuadrarían.
    /// </summary>
    public Result<TaskTimer> StopTimer(Guid timerId, Guid userId, DateTime nowUtc)
    {
        foreach (var timer in _timers)
        {
            if (timer.Id != timerId)
                continue;

            if (timer.UserId != userId)
                return Result.Failure<TaskTimer>(TaskErrors.Timer.NotOwner);

            if (!timer.IsRunning)
                return Result.Failure<TaskTimer>(TaskErrors.Timer.NotRunning);

            timer.Stop(nowUtc);
            ActualHours += timer.Hours;
            return Result.Success(timer);
        }

        return Result.Failure<TaskTimer>(TaskErrors.Timer.NotFound);
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

    internal void RegisterBlockerAdded() => OpenBlockerCount++;

    /// <summary>Nunca baja de 0: un evento reprocesado no puede desbloquear una tarea que sigue bloqueada.</summary>
    internal void RegisterBlockerResolved(DateTime nowUtc)
    {
        if (OpenBlockerCount == 0)
            return;

        OpenBlockerCount--;
        if (OpenBlockerCount == 0)
            AddDomainEvent(new TaskUnblockedDomainEvent(Id, TenantId, AssigneeUserId, nowUtc));
    }

    /// <summary>Un bloqueador ya resuelto se reabrió y vuelve a bloquear.</summary>
    internal void RegisterBlockerReopened() => OpenBlockerCount++;

    internal void RegisterSubtaskOpened() => OpenSubtaskCount++;

    internal void RegisterSubtaskClosed()
    {
        if (OpenSubtaskCount > 0)
            OpenSubtaskCount--;
    }

    internal void RegisterSubtaskReopened() => OpenSubtaskCount++;

    /// <summary>
    /// Fija los contadores contra la verdad de las filas. Separado de los <c>Register*</c> porque un
    /// ajuste no es un hecho de negocio y no emite eventos.
    /// </summary>
    /// <summary>
    /// Lo llama el barrido de vencidos tras publicar el aviso. Público como los <c>MarkAttachment*</c>
    /// y por lo mismo: lo dispara un proceso de fondo, no una acción del usuario. Idempotente.
    /// </summary>
    public bool MarkOverdueNotified(DateTime nowUtc)
    {
        if (OverdueNotifiedAtUtc is not null)
            return false;

        OverdueNotifiedAtUtc = nowUtc;
        return true;
    }

    internal void ReconcileCounters(int openBlockerCount, int openSubtaskCount)
    {
        OpenBlockerCount = openBlockerCount < 0 ? 0 : openBlockerCount;
        OpenSubtaskCount = openSubtaskCount < 0 ? 0 : openSubtaskCount;
    }

    /// <summary>
    /// Enlaza un archivo que ya está en CloudStorage —el caso dominante: el W-2 lo subió el cliente
    /// por el portal antes de que existiera la tarea—.
    /// </summary>
    public Result<TaskAttachment> LinkExistingFile(
        Guid fileId,
        string? displayName,
        string? contentType,
        long sizeBytes,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var allowed = EnsureCanAttach(fileId, displayName);
        if (allowed.IsFailure)
            return Result.Failure<TaskAttachment>(allowed.Error);

        var attachment = TaskAttachment.Link(
            Id,
            TenantId,
            fileId,
            allowed.Value,
            contentType,
            sizeBytes,
            byUserId,
            nowUtc
        );
        _attachments.Add(attachment);
        return Result.Success(attachment);
    }

    /// <summary>Recién subido a CloudStorage: queda pendiente hasta que el escaneo se pronuncie.</summary>
    public Result<TaskAttachment> AttachUploadedFile(
        Guid fileId,
        string? displayName,
        string? contentType,
        long sizeBytes,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var allowed = EnsureCanAttach(fileId, displayName);
        if (allowed.IsFailure)
            return Result.Failure<TaskAttachment>(allowed.Error);

        var attachment = TaskAttachment.Upload(
            Id,
            TenantId,
            fileId,
            allowed.Value,
            contentType,
            sizeBytes,
            byUserId,
            nowUtc
        );
        _attachments.Add(attachment);
        return Result.Success(attachment);
    }

    /// <summary>
    /// El archivo de referencia del guion. Nace <c>Available</c> igual que un enlazado —ya está en
    /// CloudStorage y ya fue escaneado— y lo comparten todas las instancias de la plantilla: un solo
    /// objeto, N referencias.
    /// </summary>
    internal Result<TaskAttachment> AttachTemplateFile(
        Guid fileId,
        string displayName,
        string? contentType,
        long sizeBytes,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var allowed = EnsureCanAttach(fileId, displayName);
        if (allowed.IsFailure)
            return Result.Failure<TaskAttachment>(allowed.Error);

        var attachment = TaskAttachment.FromTemplate(
            Id,
            TenantId,
            fileId,
            allowed.Value,
            contentType,
            sizeBytes,
            byUserId,
            nowUtc
        );
        _attachments.Add(attachment);
        return Result.Success(attachment);
    }

    /// <summary>
    /// Los tres <c>MarkAttachment*</c> devuelven <c>false</c> cuando el archivo no es de esta tarea:
    /// el consumer recibe los eventos de todo el monorepo y tirar excepción llenaría la DLQ con
    /// archivos ajenos.
    /// </summary>
    public bool MarkAttachmentAvailable(Guid fileId) => FindActive(fileId)?.MarkAvailable() ?? false;

    public bool MarkAttachmentRejected(Guid fileId, string reason, DateTime nowUtc)
    {
        if (FindActive(fileId) is not { } attachment || !attachment.MarkRejected(reason, nowUtc))
            return false;

        AddDomainEvent(
            new TaskAttachmentRejectedDomainEvent(
                Id,
                TenantId,
                attachment.Id,
                fileId,
                attachment.DisplayName,
                reason,
                attachment.AttachedByUserId,
                nowUtc
            )
        );
        return true;
    }

    /// <summary>Lo borraron desde CloudStorage: la tarea no puede seguir mostrando el archivo.</summary>
    public bool MarkAttachmentDetached(Guid fileId, DateTime nowUtc) => FindActive(fileId)?.Detach(nowUtc) ?? false;

    /// <summary>
    /// El usuario lo quita de la tarea. El byte sigue en CloudStorage: Task no es dueño del archivo
    /// y borrarlo se llevaría por delante al resto de servicios que lo referencian.
    /// </summary>
    public Result DetachFile(Guid fileId, DateTime nowUtc)
    {
        if (FindActive(fileId) is not { } attachment)
            return Result.Failure(TaskErrors.Attachment.NotFound);

        attachment.Detach(nowUtc);
        return Result.Success();
    }

    private Result<string> EnsureCanAttach(Guid fileId, string? displayName)
    {
        if (IsClosed)
            return Result.Failure<string>(TaskErrors.Attachment.TaskClosed);

        if (fileId == Guid.Empty)
            return Result.Failure<string>(TaskErrors.Attachment.FileRequired);

        var trimmed = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return Result.Failure<string>(TaskErrors.Attachment.DisplayNameRequired);

        if (trimmed.Length > 260)
            return Result.Failure<string>(TaskErrors.Attachment.DisplayNameTooLong);

        if (FindActive(fileId) is not null)
            return Result.Failure<string>(TaskErrors.Attachment.Duplicate);

        return _attachments.Count(a => a.IsActive) >= MaxActiveAttachments
            ? Result.Failure<string>(TaskErrors.Attachment.LimitReached)
            : Result.Success(trimmed);
    }

    private TaskAttachment? FindActive(Guid fileId) =>
        _attachments.FirstOrDefault(a => a.FileId == fileId && a.IsActive);

    private bool IsClosed => Status is TaskItemStatus.Completed or TaskItemStatus.Cancelled;
}
