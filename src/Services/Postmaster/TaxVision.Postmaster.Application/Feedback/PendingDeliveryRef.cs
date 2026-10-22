namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>Un destinatario que todavía no tiene outcome final: lo que un poller necesita para preguntar por él.</summary>
public sealed record PendingDeliveryRef(
    Guid SentMessageId,
    Guid RecipientId,
    string RecipientAddress,
    string? ProviderMessageId,
    DateTime SentAtUtc
);
