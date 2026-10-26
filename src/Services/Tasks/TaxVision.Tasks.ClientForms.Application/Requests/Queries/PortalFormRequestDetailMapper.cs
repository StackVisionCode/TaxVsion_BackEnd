using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Queries;

// Detalle que ve quien responde, sea cliente del portal o participante externo.
internal static class PortalFormRequestDetailMapper
{
    private const string EmptyAnswers = "{\"schemaVersion\":1,\"values\":{}}";

    internal static PortalFormRequestDetailResponse Map(
        FormRequest request,
        FormTemplateVersion version,
        IAnswersProtector protector,
        IReadOnlyList<FormConsentPresentationResponse> consents
    )
    {
        var answers = request.Submission?.DraftAnswers is { } draft ? protector.Unprotect(draft) : EmptyAnswers;
        return new PortalFormRequestDetailResponse(
            request.Id,
            request.Title,
            request.MessageToClient,
            request.Status,
            request.Status is not (FormRequestStatus.Pending or FormRequestStatus.Returned),
            request.DueAtUtc,
            request.TaxYear,
            FormJson.Parse(version.Definition.Json),
            FormJson.Parse(answers),
            Convert.ToBase64String(request.RowVersion),
            MapAttachments(request),
            consents,
            MapReview(request)
        );
    }

    // Solo una solicitud devuelta muestra los comentarios de su revisión vigente.
    private static PortalFormReviewResponse? MapReview(FormRequest request)
    {
        var revision = request.Submission?.Revisions.SingleOrDefault(item =>
            item.RevisionNumber == request.CurrentRevisionNumber
        );
        if (request.Status != FormRequestStatus.Returned || revision is null)
            return null;
        return new PortalFormReviewResponse(
            revision.ReviewMessage,
            revision
                .FieldComments.Select(item => new FormFieldCommentResponse(item.ComponentId, item.RowId, item.Comment))
                .ToArray()
        );
    }

    private static PortalFormAttachmentResponse[] MapAttachments(FormRequest request) =>
        request
            .Submission?.Attachments.Select(item => new PortalFormAttachmentResponse(
                item.Id,
                item.FileId,
                item.ComponentId,
                item.RowId,
                item.DisplayName,
                item.Status,
                item.RejectionCode
            ))
            .ToArray()
        ?? [];
}
