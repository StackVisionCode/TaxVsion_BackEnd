using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// Published by Communication (Node.js) when a tenant user opens a cross-tenant support ticket.
/// The office tenant is <see cref="IntegrationEvent.TenantId"/>; <see cref="AgentTenantId"/> is the
/// platform/support tenant where mobile support agents register their push devices.
/// </summary>
[MessageIdentity("communication.support.opened.v1")]
public sealed record SupportOpenedIntegrationEvent : IntegrationEvent
{
    public required Guid TicketId { get; init; }
    public required Guid AgentTenantId { get; init; }
    public IReadOnlyList<Guid> SupportRecipientUserIds { get; init; } = [];
    public required Guid OpenedByUserId { get; init; }
    public required Guid ConversationId { get; init; }
    public required string Subject { get; init; }
    public required string Category { get; init; }
    public required string Priority { get; init; }
}
