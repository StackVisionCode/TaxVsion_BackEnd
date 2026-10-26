using System.Text.Json;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Queries;

public sealed record ListPortalFormRequestsQuery(Guid TenantId, Guid CustomerId, bool OnlyOpen);

public sealed record PortalFormRequestSummaryResponse(
    Guid Id,
    string Title,
    FormRequestStatus Status,
    DateTime? DueAtUtc,
    bool IsOverdue,
    int ProgressPercent,
    DateTime? LastSubmittedAtUtc,
    bool HasReviewMessage
);

public sealed record GetPortalFormRequestQuery(Guid TenantId, Guid PortalUserId, Guid CustomerId, Guid RequestId);

public sealed record PortalFormAttachmentResponse(
    Guid Id,
    Guid FileId,
    string ComponentId,
    string? RowId,
    string DisplayName,
    FormAttachmentStatus Status,
    string? RejectionCode
);

public sealed record PortalFormReviewResponse(string? Message, IReadOnlyList<FormFieldCommentResponse> FieldComments);

public sealed record FormFieldCommentResponse(string ComponentId, string? RowId, string Comment);

public sealed record PortalFormRequestDetailResponse(
    Guid Id,
    string Title,
    string? MessageToClient,
    FormRequestStatus Status,
    bool ReadOnly,
    DateTime? DueAtUtc,
    int? TaxYear,
    JsonElement Definition,
    JsonElement Answers,
    string RowVersion,
    IReadOnlyList<PortalFormAttachmentResponse> Attachments,
    IReadOnlyList<FormConsentPresentationResponse> Consents,
    PortalFormReviewResponse? Review
);
