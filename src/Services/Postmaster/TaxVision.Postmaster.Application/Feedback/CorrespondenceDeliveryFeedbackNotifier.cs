using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>
/// Traduce un cambio de entrega de un correo enviado desde el CRM al evento que entiende
/// Correspondence. Uno por destinatario y por cambio de resultado.
/// </summary>
internal static class CorrespondenceDeliveryFeedbackNotifier
{
    /// <param name="suppressed">Este aviso dejó la dirección en la lista de supresión.</param>
    public static PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent? EventFor(
        SentMessage message,
        SentMessageRecipient recipient,
        DeliveryFeedback feedback,
        DeliveryReportRef? report,
        bool suppressed,
        string correlationId
    )
    {
        if (message.CorrespondenceDraftId is not { } draftId || feedback.Kind != DeliveryFeedbackKind.Outcome)
            return null;

        var outcome = OutcomeOf(feedback.Outcome!.Value);
        if (outcome is null)
            return null;

        return new PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent
        {
            TenantId = message.TenantId,
            CorrelationId = correlationId,
            SentMessageId = message.Id,
            CorrespondenceDraftId = draftId,
            Recipient = recipient.Address,
            Outcome = outcome,
            Category = CategoryOf(feedback.FailureCategory),
            StatusCode = feedback.StatusCode?.ToString(),
            DiagnosticSnippet = feedback.Diagnostic,
            Suppressed = suppressed,
            ReportAccountId = report?.AccountId,
            ReportProviderMessageId = report?.ProviderMessageId,
            EventAtUtc = feedback.OccurredAtUtc,
        };
    }

    /// <summary>
    /// La queja no va al Inbox: el correo sí llegó (el destinatario lo marcó como spam), no hay nada
    /// que corregir ni reenviar. La supresión que deja se ve y se quita en la lista de supresión.
    /// </summary>
    private static string? OutcomeOf(DeliveryOutcome outcome) =>
        outcome switch
        {
            DeliveryOutcome.Delivered => CorrespondenceDeliveryOutcomes.Delivered,
            DeliveryOutcome.Deferred => CorrespondenceDeliveryOutcomes.Delayed,
            DeliveryOutcome.SoftBounced or DeliveryOutcome.HardBounced or DeliveryOutcome.Rejected =>
                CorrespondenceDeliveryOutcomes.NotDelivered,
            _ => null,
        };

    /// <summary>Explícito y no con ToString(): el contrato no cambia si se renombra el enum.</summary>
    private static string? CategoryOf(DeliveryFailureCategory? category) =>
        category switch
        {
            DeliveryFailureCategory.InvalidAddress => DeliveryFailureCategories.InvalidAddress,
            DeliveryFailureCategory.DomainNotFound => DeliveryFailureCategories.DomainNotFound,
            DeliveryFailureCategory.MailboxFull => DeliveryFailureCategories.MailboxFull,
            DeliveryFailureCategory.Rejected => DeliveryFailureCategories.Rejected,
            DeliveryFailureCategory.TooLarge => DeliveryFailureCategories.TooLarge,
            DeliveryFailureCategory.Delayed => DeliveryFailureCategories.Delayed,
            DeliveryFailureCategory.Other => DeliveryFailureCategories.Other,
            _ => null,
        };
}
