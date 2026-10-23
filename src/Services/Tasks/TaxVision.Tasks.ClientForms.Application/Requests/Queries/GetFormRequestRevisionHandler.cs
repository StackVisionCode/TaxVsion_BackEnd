using System.Text.Json;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Queries;

public static class GetFormRequestRevisionHandler
{
    public static async Task<Result<FormRequestRevisionResponse>> Handle(
        GetFormRequestRevisionQuery query,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IAnswersProtector protector,
        IFormConsentRecordRepository consentRecords,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var revision = await FindRevisionAsync(query, requests, visibility, ct);
        if (revision is null)
            return Result.Failure<FormRequestRevisionResponse>(FormRequestErrors.NotFound);
        var consents = await consentRecords.ListForRequestAsync(query.TenantId, query.RequestId, ct);
        await audit.RecordAnswersViewedAsync(
            query.TenantId,
            query.RequestId,
            revision.RevisionNumber,
            query.ActorUserId,
            DateTime.UtcNow,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(Response(revision, protector, consents));
    }

    private static async Task<FormSubmissionRevision?> FindRevisionAsync(
        GetFormRequestRevisionQuery query,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        CancellationToken ct
    )
    {
        var found = await requests.GetForReviewAsync(query.TenantId, query.RequestId, ct);
        if (found.IsFailure)
            return null;
        if (!await visibility.CanViewAsync(query.TenantId, query.ActorUserId, query.CanViewAll, found.Value, ct))
            return null;
        return found.Value.Submission?.Revisions.SingleOrDefault(item => item.RevisionNumber == query.RevisionNumber);
    }

    // La evidencia ligada a esta revisión o a una anterior que esta reutiliza; la pendiente aún no pertenece a ninguna.
    internal static IReadOnlyList<FormConsentEvidenceResponse> EvidenceFor(
        int revisionNumber,
        IReadOnlyList<FormConsentRecord> consents
    ) =>
        consents
            .Where(item => item.RevisionNumber.HasValue && item.RevisionNumber <= revisionNumber)
            .Select(FormConsentEvidenceResponse.From)
            .ToArray();

    private static FormRequestRevisionResponse Response(
        FormSubmissionRevision revision,
        IAnswersProtector protector,
        IReadOnlyList<FormConsentRecord> consents
    ) =>
        new(
            revision.RevisionNumber,
            revision.SubmittedAtUtc,
            revision.Outcome,
            revision.ReviewMessage,
            revision
                .FieldComments.Select(item => new FormFieldCommentResponse(item.ComponentId, item.RowId, item.Comment))
                .ToArray(),
            revision.PdfStatus,
            revision.PdfFileId,
            FormJson.Parse(protector.Unprotect(revision.Answers)),
            EvidenceFor(revision.RevisionNumber, consents)
        );
}
