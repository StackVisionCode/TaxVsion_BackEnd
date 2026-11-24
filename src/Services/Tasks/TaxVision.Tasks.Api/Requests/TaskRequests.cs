using TaxVision.Tasks.Application.ClientRequests.Commands;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Api.Requests;

/// <summary>
/// El <c>tenantId</c> y el <c>userId</c> nunca viajan en el body: salen del JWT verificado.
/// </summary>
public sealed record CreateTaskRequest(
    string? Title,
    string? Description,
    TaskPriority Priority,
    Guid? AssigneeUserId,
    Guid? CustomerId,
    int? TaxYear,
    DateTime? DueAtUtc,
    string? DueTimeZoneId,
    bool DueIsStatutory,
    decimal? EstimatedHours,
    DateOnly? DueOn = null
);

/// <summary>Sin cliente ni año fiscal: los hereda del padre.</summary>
public sealed record CreateSubtaskRequest(
    string? Title,
    string? Description,
    TaskPriority Priority,
    Guid? AssigneeUserId,
    DateTime? DueAtUtc,
    string? DueTimeZoneId,
    bool DueIsStatutory,
    decimal? EstimatedHours,
    DateOnly? DueOn = null
);

public sealed record UpdateTaskDetailsRequest(string? Title, string? Description);

public sealed record ChangeTaskPriorityRequest(TaskPriority Priority);

/// <summary>El formulario de edición entero y la versión que el usuario vio (<c>RowVersion</c> en base64).</summary>
/// <param name="StatutoryChangeReason">Obligatoria sólo si se afloja un vencimiento estatutario.</param>
public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    TaskPriority Priority,
    DateOnly? DueOn,
    bool IsStatutory,
    string? StatutoryChangeReason,
    Guid? AssigneeUserId,
    int? EstimatedMinutes,
    Guid? CustomerId,
    int? TaxYear,
    string? RowVersion
);

/// <param name="StatutoryChangeReason">Obligatoria sólo si se afloja un vencimiento estatutario.</param>
/// <param name="DueOn">Fecha civil: si viene, manda sobre el instante y vence al final de ese día en la oficina.</param>
public sealed record ChangeTaskDueRequest(
    DateTime? DueAtUtc,
    string? TimeZoneId,
    bool IsStatutory,
    string? StatutoryChangeReason,
    DateOnly? DueOn = null
);

/// <summary>Cuerpo opcional: sin él se completa solo la tarea, como siempre.</summary>
public sealed record CompleteTaskRequest(bool CompleteOpenSubtasks = false);

/// <summary>Cuerpo opcional: con <c>ReopenParent</c> también se reabren las tareas madre cerradas.</summary>
public sealed record ReopenTaskRequest(bool ReopenParent = false);

public sealed record CancelTaskRequest(string? Reason);

public sealed record AssignTaskRequest(Guid AssigneeUserId);

public sealed record WaitOnClientRequest(string? ExpectedItems, DateTime? ClientDueAtUtc, DateOnly? ClientDueOn = null);

public sealed record AddDependencyRequest(Guid DependsOnTaskId);

public sealed record StartTimerRequest(bool IsBillable);

/// <summary>Tiempo ya trabajado: el día (en la oficina), los minutos y si se factura.</summary>
public sealed record LogTimeRequest(DateOnly WorkedOn, int Minutes, bool IsBillable, string? Note);

public sealed record EditTimeEntryRequest(int Minutes, bool IsBillable, string? Note);

/// <summary>Null quita la estimación.</summary>
public sealed record ChangeEstimateRequest(int? EstimatedMinutes);

public sealed record UpsertTaskLabelRequest(
    string? Code,
    string? DisplayName,
    string? Color,
    TaskItemStatus MapsToStatus,
    int SortOrder
);

/// <param name="AnchorUtc">La primera ocurrencia: la serie no la calcula, la respeta.</param>
/// <param name="AnchorOn">Fecha civil: si viene, manda; vence al final de ese día en la zona de la oficina.</param>
public sealed record CreateTaskSeriesRequest(
    string? Title,
    string? Description,
    TaskPriority Priority,
    Guid? CustomerId,
    int? TaxYear,
    decimal? EstimatedHours,
    Guid? AssigneeUserId,
    bool IsStatutory,
    string? Rule,
    string? TimeZoneId,
    RecurrenceMode Mode,
    DateTime AnchorUtc,
    DateTime? EndsAtUtc,
    int? MaxOccurrences,
    DateOnly? AnchorOn = null
);

public sealed record SaveTaskTemplateStepRequest(
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

public sealed record SaveTaskTemplateRequest(
    string? Name,
    string? Description,
    string? RecurrenceRule,
    string? RecurrenceTimeZoneId,
    RecurrenceMode RecurrenceMode,
    IReadOnlyList<SaveTaskTemplateStepRequest> Steps
);

public sealed record SetTaskTemplateActiveRequest(bool IsActive);

public sealed record ApplyTaskTemplateRequest(
    Guid? AssigneeUserId,
    Guid? CustomerId,
    int? TaxYear,
    DateTime DueAtUtc,
    string? TimeZoneId,
    bool AllowDuplicate,
    DateOnly? DueOn = null
);

/// <param name="SizeBytes">Lo informa CloudStorage; sirve para mostrarlo, no para validar el byte.</param>
public sealed record LinkTaskAttachmentRequest(Guid FileId, string? DisplayName, string? ContentType, long SizeBytes);

public sealed record UploadTaskAttachmentRequest(Guid FileId, string? DisplayName, string? ContentType, long SizeBytes);

public sealed record SaveTaskTemplateAttachmentRequest(
    Guid FileId,
    string? DisplayName,
    string? ContentType,
    long SizeBytes,
    int? StepOrder
);

public sealed record SaveTaskTemplateAttachmentsRequest(IReadOnlyList<SaveTaskTemplateAttachmentRequest> Attachments);

public sealed record CreateClientRequestRequest(
    Guid CustomerId,
    Guid? TaskId,
    string? Title,
    string? Details,
    DateTime? DueAtUtc,
    DateOnly? DueOn = null
);

public sealed record ResolveClientRequestRequest(ClientRequestResolution Resolution, string? Note);

/// <param name="FileId">El id que devolvio CloudStorage al subir. Por Task no pasa el byte.</param>
public sealed record SubmitClientDocumentRequest(Guid FileId, string? DisplayName, string? ContentType, long SizeBytes);

/// <summary>Alta de un estado: el código lo genera el servidor a partir del nombre.</summary>
public sealed record CreateWorkflowStatusRequest(
    string? DisplayName,
    string? Color,
    TaskItemStatus Category,
    string? Description
);

/// <summary><c>RowVersion</c> tal como llegó en la lista: si otro lo cambió entretanto, 409.</summary>
public sealed record UpdateWorkflowStatusRequest(
    string? DisplayName,
    string? Color,
    string? Description,
    string? RowVersion
);

public sealed record ReorderWorkflowStatusesRequest(TaskItemStatus Category, IReadOnlyList<string> Codes);

/// <summary>Destino de las tareas abiertas; obligatorio si las hay.</summary>
public sealed record ArchiveWorkflowStatusRequest(string? MoveOpenTasksTo);

public sealed record MoveTaskWaitingOnClient(string? ExpectedItems, DateOnly? ClientDueOn);

/// <summary>El movimiento único: el código del estado destino y lo que pida su etapa.</summary>
public sealed record MoveTaskToStatusRequest(
    string Status,
    MoveTaskWaitingOnClient? WaitingOnClient,
    string? CancelReason,
    bool CompleteOpenSubtasks = false
);

/// <summary>Lo que el constructor de recurrencias quiere ver antes de guardar.</summary>
/// <summary>PUT /tasks/series/{id}. <c>StartsOn</c> solo cuenta si cambian la regla o el modo.</summary>
public sealed record UpdateTaskSeriesRequest(
    string? Title,
    string? Description,
    TaskPriority Priority,
    Guid AssigneeUserId,
    bool IsStatutory,
    string? Rule,
    RecurrenceMode Mode,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    int? MaxOccurrences,
    bool ApplyToOpenInstance,
    Guid? CustomerId,
    string? RowVersion
);

public sealed record PreviewTaskSeriesRequest(
    string? Rule,
    Domain.Series.RecurrenceMode Mode,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    int? MaxOccurrences,
    int? Count
);
