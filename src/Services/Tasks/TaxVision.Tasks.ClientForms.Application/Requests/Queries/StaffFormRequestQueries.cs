using System.Text.Json;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Queries;

public sealed record ListFormRequestsQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    Guid? CustomerId,
    FormParticipantKind? ParticipantKind,
    Guid? ResponsibleUserId,
    IReadOnlyCollection<FormRequestStatus>? Statuses,
    Guid? TemplateId,
    bool? Overdue,
    string? Search,
    int Page,
    int PageSize,
    FormRequestInboxView View = FormRequestInboxView.All
);

public enum FormRequestInboxView
{
    All = 1,
    Clients = 2,
    External = 3,
    NeedsAction = 4,
}

public sealed record FormRequestListItemResponse(
    Guid Id,
    FormRequestParticipantResponse Participant,
    CustomerLinkStatus CustomerLinkStatus,
    Guid? ResponsibleUserId,
    string DisplayName,
    string TemplateName,
    string? MaskedEmail,
    DateTime? ReceivedAtUtc,
    string Title,
    FormRequestStatus Status,
    int VersionNumber,
    DateTime? DueAtUtc,
    bool IsOverdue,
    int ProgressPercent,
    int CurrentRevisionNumber,
    DateTime? LastSubmittedAtUtc,
    Guid RequestedByUserId,
    DateTime RequestedAtUtc,
    IReadOnlyList<FormActionRunResponse> ActionRuns,
    FormRequestOrigin Origin,
    // P-18 — estado de la firma de la última revisión, para el chip de la bandeja.
    FormRevisionSignatureStatus LatestSignatureStatus
);

public sealed record GetFormRequestQuery(Guid TenantId, Guid ActorUserId, bool CanViewAll, Guid RequestId);

// P-13.2 — campos mapeables a UpdateCustomerProfile de la versión fijada, para el checklist del CRM.
public sealed record ListProfileUpdateFieldsQuery(Guid TenantId, Guid ActorUserId, bool CanViewAll, Guid RequestId);

public sealed record ProfileUpdateFieldResponse(
    string ComponentId,
    string Label,
    string TargetKey,
    string TargetDisplayName,
    string DataType,
    bool RequiresAdmin,
    bool IsTaxId
);

public sealed record ProfileUpdateFieldsResponse(
    FormProfileUpdateStatus Status,
    IReadOnlyList<ProfileUpdateFieldResponse> Fields,
    IReadOnlyList<FormProfileUpdateOutcomeResponse> Outcomes
);

public sealed record GetFormRequestRevisionQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    Guid RequestId,
    int RevisionNumber
);

public sealed record StaffFormAttachmentResponse(
    Guid Id,
    string ComponentId,
    string? RowId,
    Guid FileId,
    string DisplayName,
    FormAttachmentStatus Status,
    string? RejectionCode
);

public sealed record FormRequestRevisionSummaryResponse(
    int Number,
    DateTime SubmittedAtUtc,
    FormRevisionOutcome Outcome,
    string? ReviewMessage,
    IReadOnlyList<FormFieldCommentResponse> FieldComments,
    FormRevisionPdfStatus PdfStatus,
    Guid? PdfFileId,
    FormRevisionSignatureStatus SignatureStatus
);

public sealed record FormRequestRevisionResponse(
    int Number,
    DateTime SubmittedAtUtc,
    FormRevisionOutcome Outcome,
    string? ReviewMessage,
    IReadOnlyList<FormFieldCommentResponse> FieldComments,
    FormRevisionPdfStatus PdfStatus,
    Guid? PdfFileId,
    FormRevisionSignatureStatus SignatureStatus,
    JsonElement Answers,
    IReadOnlyList<FormConsentEvidenceResponse> Consents
);

public sealed record FormRequestTimelineEntryResponse(
    string Action,
    string Outcome,
    Guid? ActorUserId,
    string ActorType,
    DateTime OccurredAtUtc
);

public sealed record StaffFormRequestDetailResponse(
    FormRequestResponse Request,
    JsonElement Definition,
    int ProgressPercent,
    IReadOnlyList<FormRequestRevisionSummaryResponse> Revisions,
    JsonElement? LatestRevisionAnswers,
    IReadOnlyList<StaffFormAttachmentResponse> Attachments,
    IReadOnlyList<FormConsentEvidenceResponse> Consents,
    IReadOnlyList<FormRequestTimelineEntryResponse> Timeline,
    IReadOnlyList<FormActionRunResponse> ActionRuns,
    FormCustomerLinkResponse CustomerLink,
    string DisplayName,
    string? MaskedEmail
);

public sealed record FormRequestParticipantLabel(string DisplayName, string? MaskedEmail);
