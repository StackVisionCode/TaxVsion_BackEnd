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

/// <summary>
/// Mensaje en un ticket. <c>TenantId</c> = oficina; las filas de los agentes van a <see cref="AgentTenantId"/>.
/// Sin texto ni asunto.
/// </summary>
[MessageIdentity("communication.support.message_added.v1")]
public sealed record SupportMessageAddedIntegrationEvent : IntegrationEvent
{
    public required Guid TicketId { get; init; }
    public required Guid MessageId { get; init; }
    public required Guid SenderUserId { get; init; }
    public required string SenderRole { get; init; }
    public required DateTime SentAtUtc { get; init; }
    public required Guid ConversationId { get; init; }
    public required Guid AgentTenantId { get; init; }
    public required Guid OpenedByUserId { get; init; }
    public Guid? AssignedAgentId { get; init; }
    public IReadOnlyList<Guid> SupportRecipientUserIds { get; init; } = [];
}

/// <summary>Un agente tomó el ticket. Los tres últimos campos son opcionales: eventos viejos no los traen.</summary>
[MessageIdentity("communication.support.claimed.v1")]
public sealed record SupportClaimedIntegrationEvent : IntegrationEvent
{
    public required Guid TicketId { get; init; }
    public required Guid AssignedAgentId { get; init; }
    public required DateTime ClaimedAtUtc { get; init; }
    public Guid? OpenedByUserId { get; init; }
    public Guid? AgentTenantId { get; init; }
    public Guid? ConversationId { get; init; }
}

[MessageIdentity("communication.support.resolved.v1")]
public sealed record SupportResolvedIntegrationEvent : IntegrationEvent
{
    public required Guid TicketId { get; init; }
    public required Guid ResolvedByUserId { get; init; }
    public required bool ResolvedByAgent { get; init; }
    public required DateTime ResolvedAtUtc { get; init; }
    public Guid? OpenedByUserId { get; init; }
    public Guid? AgentTenantId { get; init; }
    public Guid? ConversationId { get; init; }
}

[MessageIdentity("communication.support.closed.v1")]
public sealed record SupportClosedIntegrationEvent : IntegrationEvent
{
    public required Guid TicketId { get; init; }
    public required Guid ClosedByUserId { get; init; }
    public required DateTime ClosedAtUtc { get; init; }
    public Guid? OpenedByUserId { get; init; }
    public Guid? AgentTenantId { get; init; }
    public Guid? ConversationId { get; init; }
}
