using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;

/// <summary>
/// Correspondence persistió un <c>IncomingEmail</c> de un customer conocido (Correspondence
/// Fase 4). Futuro/opcional per Correspondence_Service_Design_And_Implementation_Plan.md §19 —
/// hoy no tiene consumers; existe para que Notification decida más adelante si notifica al
/// usuario del tenant. Nunca lleva body ni attachments, solo lo necesario para un badge/toast.
/// </summary>
[MessageIdentity("correspondence.customer_email_received.v1")]
public sealed record CorrespondenceCustomerEmailReceivedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid IncomingEmailId { get; init; }
    public required Guid EmailThreadId { get; init; }
    public required string Subject { get; init; }
}
