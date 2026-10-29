using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

public sealed record FormRequestParticipantResponse(FormParticipantKind Kind, Guid? CustomerId, Guid? ParticipantId);

public sealed record FormProfileUpdateOutcomeResponse(string Target, bool Applied, string? FailureCode);

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
    // Directed o IntakeLink: el CRM ofrece "Resend link" solo en las dirigidas.
    FormRequestOrigin Origin,
    int ProgressPercent,
    int CurrentRevisionNumber,
    DateTime? LastSubmittedAtUtc,
    // Contador y último envío de recordatorio, para el "Reminders sent: N/5" del CRM.
    int ReminderCount,
    DateTime? LastReminderAtUtc,
    bool IsOverdue,
    // P-13 — estado de "apply to client profile" y motivos, para el drawer.
    FormProfileUpdateStatus ProfileUpdateStatus,
    IReadOnlyList<FormProfileUpdateOutcomeResponse> ProfileUpdateOutcomes,
    string RowVersion
)
{
    public static FormRequestResponse From(FormRequest request) => From(request, DateTime.UtcNow);

    public static FormRequestResponse From(FormRequest request, DateTime nowUtc) =>
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
            request.Origin,
            request.DraftProgressPercent,
            request.CurrentRevisionNumber,
            request.LastSubmittedAtUtc,
            request.ReminderCount,
            request.LastReminderAtUtc,
            request.DueAtUtc < nowUtc
                && request.Status != FormRequestStatus.Completed
                && request.Status != FormRequestStatus.Cancelled,
            request.ProfileUpdateStatus,
            request
                .ProfileUpdateOutcomes.Select(outcome => new FormProfileUpdateOutcomeResponse(
                    outcome.Target,
                    outcome.Applied,
                    outcome.FailureCode
                ))
                .ToArray(),
            Convert.ToBase64String(request.RowVersion)
        );
}
