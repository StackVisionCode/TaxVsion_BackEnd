using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

public sealed record FormRequestParticipantResponse(FormParticipantKind Kind, Guid? CustomerId, Guid? ParticipantId);

public sealed record FormRequestResponse(
    Guid Id,
    FormRequestParticipantResponse Participant,
    CustomerLinkStatus CustomerLinkStatus,
    Guid? ResponsibleUserId,
    Guid FormTemplateId,
    Guid FormTemplateVersionId,
    int VersionNumber,
    string Title,
    string? MessageToClient,
    int? TaxYear,
    Guid? TaskId,
    DateTime? DueAtUtc,
    FormRequestStatus Status,
    int ProgressPercent,
    int CurrentRevisionNumber,
    DateTime? LastSubmittedAtUtc,
    string RowVersion
)
{
    public static FormRequestResponse From(FormRequest request) =>
        new(
            request.Id,
            new FormRequestParticipantResponse(request.ParticipantKind, request.CustomerId, request.ParticipantId),
            request.CustomerLinkStatus,
            request.ResponsibleUserId,
            request.FormTemplateId,
            request.FormTemplateVersionId,
            request.VersionNumber,
            request.Title,
            request.MessageToClient,
            request.TaxYear,
            request.TaskId,
            request.DueAtUtc,
            request.Status,
            request.DraftProgressPercent,
            request.CurrentRevisionNumber,
            request.LastSubmittedAtUtc,
            Convert.ToBase64String(request.RowVersion)
        );
}
