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

public sealed record FormFieldCommentRequest(string ComponentId, string? RowId, string Comment);

public sealed record ReturnFormRequestRequest(
    string Message,
    IReadOnlyList<FormFieldCommentRequest> FieldComments,
    string RowVersion
);

public sealed record CompleteFormRequestRequest(string? InternalNote, string RowVersion);

public sealed record CancelFormRequestRequest(string? Note, string RowVersion);

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
