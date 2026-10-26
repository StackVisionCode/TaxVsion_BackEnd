using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

public static class FormRequestIntegrationEventFactory
{
    public static FormRequestAssignedIntegrationEvent Assigned(FormRequest request, string correlationId) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            FormRequestId = request.Id,
            Participant = Participant(request),
            Title = request.Title,
            HasMessage = !string.IsNullOrWhiteSpace(request.MessageToClient),
            DueAtUtc = request.DueAtUtc,
            TaxYear = request.TaxYear,
            RequestedByUserId = request.RequestedByUserId,
            FormTemplateId = request.FormTemplateId,
            VersionNumber = request.VersionNumber,
        };

    public static async Task<FormRequestSubmittedIntegrationEvent> SubmittedAsync(
        FormRequest request,
        FormSubmissionRevision revision,
        IClientFormsCustomerReader customers,
        string correlationId,
        CancellationToken ct = default
    ) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            FormRequestId = request.Id,
            Participant = Participant(request),
            ResponsibleUserId = request.ResponsibleUserId,
            CustomerDisplayName = await CustomerDisplayNameAsync(request, customers, ct),
            Title = request.Title,
            RevisionNumber = revision.RevisionNumber,
            IsResubmission = revision.RevisionNumber > 1,
            RequestedByUserId = request.RequestedByUserId,
            SubmittedBy = Actor(revision.SubmittedBy),
        };

    public static FormRequestReturnedIntegrationEvent Returned(
        FormRequest request,
        FormSubmissionRevision revision,
        Guid returnedByUserId,
        string correlationId
    ) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            FormRequestId = request.Id,
            Participant = Participant(request),
            Title = request.Title,
            RevisionNumber = revision.RevisionNumber,
            FieldCommentCount = revision.FieldComments.Count,
            ReturnedByUserId = returnedByUserId,
            DueAtUtc = request.DueAtUtc,
        };

    public static FormRequestCompletedIntegrationEvent Completed(
        FormRequest request,
        Guid completedByUserId,
        string correlationId
    ) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            FormRequestId = request.Id,
            Participant = Participant(request),
            Title = request.Title,
            RevisionNumber = request.CurrentRevisionNumber,
            CompletedByUserId = completedByUserId,
        };

    public static FormRequestCancelledIntegrationEvent Cancelled(
        FormRequest request,
        Guid? cancelledByUserId,
        bool notifyClient,
        string correlationId
    ) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            FormRequestId = request.Id,
            Participant = Participant(request),
            Title = request.Title,
            Reason = request.CancelReason switch
            {
                FormCancelReason.CustomerArchived => "customer_archived",
                FormCancelReason.Archived => "archived",
                _ => "staff_cancelled",
            },
            CancelledByUserId = cancelledByUserId,
            NotifyClient = notifyClient,
        };

    public static async Task<FormAttachmentRejectedIntegrationEvent> AttachmentRejectedAsync(
        FormRequest request,
        FormAttachment attachment,
        IClientFormsCustomerReader customers,
        string correlationId,
        CancellationToken ct = default
    )
    {
        string? customerDisplayName = null;
        if (request.EffectiveCustomerId is { } customerId)
            customerDisplayName = (await customers.GetAsync(request.TenantId, customerId, ct))?.DisplayName;

        var extension = Path.GetExtension(attachment.DisplayName).TrimStart('.').ToLowerInvariant();
        return new FormAttachmentRejectedIntegrationEvent
        {
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            FormRequestId = request.Id,
            Participant = Participant(request),
            ResponsibleUserId = request.ResponsibleUserId,
            CustomerDisplayName = customerDisplayName,
            Title = request.Title,
            AttachmentId = attachment.Id,
            FileId = attachment.FileId,
            FileExtension = extension.Length == 0 ? "unknown" : extension,
            ReasonCode = attachment.RejectionCode ?? "scan_failed",
            RequestedByUserId = request.RequestedByUserId,
        };
    }

    public static ClientFormParticipant Participant(FormRequest request) =>
        new()
        {
            Kind = request.ParticipantKind == FormParticipantKind.Customer ? "Customer" : "External",
            CustomerId = request.EffectiveCustomerId,
            ParticipantId = request.ParticipantId,
        };

    private static ClientFormActorRef Actor(FormActor actor) =>
        new()
        {
            Kind = actor.Kind == FormActorKind.PortalUser ? "PortalUser" : "ExternalParticipant",
            PortalUserId = actor.PortalUserId,
            ParticipantId = actor.ParticipantId,
        };

    private static async Task<string?> CustomerDisplayNameAsync(
        FormRequest request,
        IClientFormsCustomerReader customers,
        CancellationToken ct
    ) =>
        request.EffectiveCustomerId is { } customerId
            ? (await customers.GetAsync(request.TenantId, customerId, ct))?.DisplayName
            : null;
}
