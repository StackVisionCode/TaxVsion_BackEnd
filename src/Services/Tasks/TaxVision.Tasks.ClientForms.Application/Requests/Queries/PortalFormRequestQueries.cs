using System.Text.Json;
using BuildingBlocks.Results;
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

public static class ListPortalFormRequestsHandler
{
    public static async Task<IReadOnlyList<PortalFormRequestSummaryResponse>> Handle(
        ListPortalFormRequestsQuery query,
        IFormRequestRepository requests,
        CancellationToken ct
    ) => (await requests.ListByCustomerAsync(query.TenantId, query.CustomerId, query.OnlyOpen, ct))
        .Select(item => new PortalFormRequestSummaryResponse(
            item.Id,
            item.Title,
            item.Status,
            item.DueAtUtc,
            item.DueAtUtc < DateTime.UtcNow && item.Status is FormRequestStatus.Pending or FormRequestStatus.Returned,
            item.DraftProgressPercent,
            item.LastSubmittedAtUtc,
            item.Status == FormRequestStatus.Returned
        ))
        .ToArray();
}

public sealed record GetPortalFormRequestQuery(Guid TenantId, Guid CustomerId, Guid RequestId);

public sealed record PortalFormAttachmentResponse(
    Guid Id,
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
    PortalFormReviewResponse? Review
);

public static class GetPortalFormRequestHandler
{
    public static async Task<Result<PortalFormRequestDetailResponse>> Handle(
        GetPortalFormRequestQuery query,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        IAnswersProtector protector,
        CancellationToken ct
    )
    {
        var found = await requests.GetForReviewAsync(query.TenantId, query.RequestId, ct);
        if (found.IsFailure || found.Value.EffectiveCustomerId != query.CustomerId)
            return Result.Failure<PortalFormRequestDetailResponse>(FormRequestErrors.NotFound);
        var version = await versions.GetByIdAsync(query.TenantId, found.Value.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure<PortalFormRequestDetailResponse>(FormRequestErrors.TemplateNotPublished);

        var answers = found.Value.Submission?.DraftAnswers is { } draft
            ? protector.Unprotect(draft)
            : "{\"schemaVersion\":1,\"values\":{}}";
        var revision = found.Value.Submission?.Revisions.SingleOrDefault(item =>
            item.RevisionNumber == found.Value.CurrentRevisionNumber
        );
        var review = found.Value.Status == FormRequestStatus.Returned && revision is not null
            ? new PortalFormReviewResponse(
                revision.ReviewMessage,
                revision.FieldComments.Select(item => new FormFieldCommentResponse(item.ComponentId, item.RowId, item.Comment)).ToArray()
            )
            : null;
        return Result.Success(new PortalFormRequestDetailResponse(
            found.Value.Id,
            found.Value.Title,
            found.Value.MessageToClient,
            found.Value.Status,
            found.Value.Status is not (FormRequestStatus.Pending or FormRequestStatus.Returned),
            found.Value.DueAtUtc,
            found.Value.TaxYear,
            JsonDocument.Parse(version.Value.Definition.Json).RootElement.Clone(),
            JsonDocument.Parse(answers).RootElement.Clone(),
            Convert.ToBase64String(found.Value.RowVersion),
            found.Value.Submission?.Attachments.Select(item => new PortalFormAttachmentResponse(
                item.Id,
                item.ComponentId,
                item.RowId,
                item.DisplayName,
                item.Status,
                item.RejectionCode
            )).ToArray() ?? [],
            review
        ));
    }
}
