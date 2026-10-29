using System.Text.Json;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Api.Requests.ClientForms;

public sealed record AssignFormRequestRequest(
    Guid CustomerId,
    Guid TemplateId,
    string? Title,
    string? MessageToClient,
    DateTime? DueAtUtc,
    int? TaxYear,
    Guid? TaskId,
    Guid? PrefillFromRequestId
);

public sealed record AssignExternalFormRequestRequest(
    string Email,
    string? FullName,
    Guid TemplateId,
    string EntryNoticeKey,
    string? Title,
    string? MessageToClient,
    DateTime? DueAtUtc,
    int? TaxYear
);

// P-13 — aplicar al perfil: campos elegidos (null = todos los mapeados) y si incluir el tax identifier (admin).
public sealed record ApplyFormProfileRequest(IReadOnlyList<string>? ComponentIds, bool IncludeTaxIdentifier);

public sealed record FormFieldCommentRequest(string ComponentId, string? RowId, string Comment);

public sealed record ReturnFormRequestRequest(
    string Message,
    IReadOnlyList<FormFieldCommentRequest> FieldComments,
    string RowVersion
);

public sealed record CompleteFormRequestRequest(string? InternalNote, string RowVersion);

public sealed record CancelFormRequestRequest(string? Note, string RowVersion);

public sealed record ArchiveFormRequestRequest(string? Note, string RowVersion);

public sealed record LinkFormRequestCustomerRequest(Guid CustomerId, string RowVersion);

public sealed record ReassignFormRequestResponsibleRequest(Guid UserId, string RowVersion);

public sealed record ChangeFormRequestDueDateRequest(DateTime? DueAtUtc, string RowVersion);

public sealed record SavePortalFormDraftRequest(JsonElement Answers, string RowVersion);

public sealed record SubmitPortalFormRequestRequest(string RowVersion);

public sealed record RegisterPortalFormAttachmentRequest(
    string ComponentId,
    string? RowId,
    Guid FileId,
    string DisplayName,
    string ContentType,
    long SizeBytes
);

public sealed record CapturePortalSensitiveAnswerRequest(
    string ComponentId,
    string? RowId,
    FormTaxIdentifierKind Kind,
    string Value
);

// Motivo opcional (<= 200), con el mismo filtro de datos sensibles que los demás textos del staff.
public sealed record MarkProspectLostRequest(string? Reason);
