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
    decimal? EstimatedHours
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
    decimal? EstimatedHours
);

public sealed record UpdateTaskDetailsRequest(string? Title, string? Description);

public sealed record ChangeTaskPriorityRequest(TaskPriority Priority);

/// <param name="StatutoryChangeReason">Obligatoria sólo si se afloja un vencimiento estatutario.</param>
public sealed record ChangeTaskDueRequest(
    DateTime? DueAtUtc,
    string? TimeZoneId,
    bool IsStatutory,
    string? StatutoryChangeReason
);

public sealed record CancelTaskRequest(string? Reason);

public sealed record AssignTaskRequest(Guid AssigneeUserId);

public sealed record WaitOnClientRequest(string? ExpectedItems, DateTime? ClientDueAtUtc);

public sealed record AddDependencyRequest(Guid DependsOnTaskId);

public sealed record StartTimerRequest(bool IsBillable);

public sealed record UpsertTaskLabelRequest(
    string? Code,
    string? DisplayName,
    string? Color,
    TaskItemStatus MapsToStatus,
    int SortOrder
);

/// <param name="AnchorUtc">La primera ocurrencia: la serie no la calcula, la respeta.</param>
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
    int? MaxOccurrences
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
    bool AllowDuplicate
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
    DateTime? DueAtUtc
);

public sealed record ResolveClientRequestRequest(ClientRequestResolution Resolution, string? Note);

/// <param name="FileId">El id que devolvio CloudStorage al subir. Por Task no pasa el byte.</param>
public sealed record SubmitClientDocumentRequest(Guid FileId, string? DisplayName, string? ContentType, long SizeBytes);
