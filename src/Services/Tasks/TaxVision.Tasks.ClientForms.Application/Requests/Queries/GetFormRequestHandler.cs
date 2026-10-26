using System.Text.Json;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Queries;

public static class GetFormRequestHandler
{
    public static async Task<Result<StaffFormRequestDetailResponse>> Handle(
        GetFormRequestQuery query,
        IFormRequestRepository requests,
        IFormRequestStaffReadRepository reads,
        IFormRequestVisibilityFilter visibility,
        IFormTemplateVersionRepository versions,
        IAnswersProtector protector,
        IFormConsentRecordRepository consentRecords,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetForReviewAsync(query.TenantId, query.RequestId, ct);
        if (
            found.IsFailure
            || !await visibility.CanViewAsync(query.TenantId, query.ActorUserId, query.CanViewAll, found.Value, ct)
        )
            return Result.Failure<StaffFormRequestDetailResponse>(FormRequestErrors.NotFound);
        var version = await versions.GetByIdAsync(query.TenantId, found.Value.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure<StaffFormRequestDetailResponse>(FormRequestErrors.TemplateNotPublished);

        var references = await LoadReferencesAsync(query, found.Value, reads, visibility, consentRecords, ct);
        var response = Build(found.Value, version.Value.Definition.Json, protector, references);
        await audit.RecordAnswersViewedAsync(
            query.TenantId,
            query.RequestId,
            found.Value.CurrentRevisionNumber,
            query.ActorUserId,
            DateTime.UtcNow,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(response);
    }

    private static async Task<DetailReferences> LoadReferencesAsync(
        GetFormRequestQuery query,
        FormRequest request,
        IFormRequestStaffReadRepository reads,
        IFormRequestVisibilityFilter visibility,
        IFormConsentRecordRepository consentRecords,
        CancellationToken ct
    ) =>
        new(
            await reads.ListTimelineAsync(query.TenantId, query.RequestId, ct),
            await consentRecords.ListForRequestAsync(query.TenantId, query.RequestId, ct),
            await FormActionRunViews.ForStaffAsync(request, query.ActorUserId, query.CanViewAll, visibility, ct),
            // Sin fila de la bandeja (deep link) el detalle trae su propio nombre y email enmascarado.
            await reads.GetParticipantLabelAsync(query.TenantId, request, ct)
        );

    private static StaffFormRequestDetailResponse Build(
        FormRequest request,
        string definitionJson,
        IAnswersProtector protector,
        DetailReferences references
    )
    {
        var revisions =
            request
                .Submission?.Revisions.OrderByDescending(item => item.RevisionNumber)
                .Select(RevisionSummary)
                .ToArray()
            ?? [];
        var latest = request.Submission?.Revisions.SingleOrDefault(item =>
            item.RevisionNumber == request.CurrentRevisionNumber
        );
        JsonElement? answers = latest is null ? null : FormJson.Parse(protector.Unprotect(latest.Answers));
        return new StaffFormRequestDetailResponse(
            FormRequestResponse.From(request),
            FormJson.Parse(definitionJson),
            request.DraftProgressPercent,
            revisions,
            answers,
            MapAttachments(request),
            references.Consents.Select(FormConsentEvidenceResponse.From).ToArray(),
            references.Timeline,
            references.Actions,
            FormCustomerLinkResponse.From(request),
            references.Label.DisplayName,
            references.Label.MaskedEmail
        );
    }

    private static StaffFormAttachmentResponse[] MapAttachments(FormRequest request) =>
        request
            .Submission?.Attachments.Select(item => new StaffFormAttachmentResponse(
                item.Id,
                item.ComponentId,
                item.RowId,
                item.FileId,
                item.DisplayName,
                item.Status,
                item.RejectionCode
            ))
            .ToArray()
        ?? [];

    private static FormRequestRevisionSummaryResponse RevisionSummary(FormSubmissionRevision revision) =>
        new(
            revision.RevisionNumber,
            revision.SubmittedAtUtc,
            revision.Outcome,
            revision.ReviewMessage,
            revision
                .FieldComments.Select(item => new FormFieldCommentResponse(item.ComponentId, item.RowId, item.Comment))
                .ToArray(),
            revision.PdfStatus,
            revision.PdfFileId
        );

    private sealed record DetailReferences(
        IReadOnlyList<FormRequestTimelineEntryResponse> Timeline,
        IReadOnlyList<TaxVision.Tasks.ClientForms.Domain.Consents.FormConsentRecord> Consents,
        IReadOnlyList<FormActionRunResponse> Actions,
        FormRequestParticipantLabel Label
    );
}
