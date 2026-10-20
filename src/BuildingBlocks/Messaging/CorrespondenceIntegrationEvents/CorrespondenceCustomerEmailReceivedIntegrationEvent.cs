using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;

/// <summary>Correspondence persistió un correo entrante de un customer conocido.</summary>
[MessageIdentity("correspondence.customer_email_received.v1")]
public sealed record CorrespondenceCustomerEmailReceivedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid IncomingEmailId { get; init; }
    public required Guid EmailThreadId { get; init; }
    public required string Subject { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? MailboxOwnerUserId { get; init; }
}
