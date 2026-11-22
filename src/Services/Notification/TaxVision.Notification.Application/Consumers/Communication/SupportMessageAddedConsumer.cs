using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// <c>communication.support.message_added.v1</c>. Oficina → agentes: in-app agrupado por ticket y push
/// genérico en el tenant de soporte. Agente → quien abrió: in-app agrupado en la oficina; el push ya
/// lo manda el camino de chat. No va detrás del flag Support: Node no produce estos kinds.
/// </summary>
public static class SupportMessageAddedConsumer
{
    private const string EventType = "communication.support.message_added.v1";

    public static async Task Handle(
        SupportMessageAddedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.TenantId == Guid.Empty || evt.AgentTenantId == Guid.Empty)
                return;

            if (string.Equals(evt.SenderRole, "Agent", StringComparison.OrdinalIgnoreCase))
            {
                if (evt.OpenedByUserId != Guid.Empty && evt.OpenedByUserId != evt.SenderUserId)
                    await RecordAsync(
                        dispatcher,
                        correlation,
                        evt,
                        evt.TenantId,
                        evt.OpenedByUserId,
                        NotificationKinds.CommunicationSupportReplyReceived,
                        "Support replied to your ticket",
                        ct
                    );
                return;
            }

            var agents = await AgentsAsync(evt, recipientResolver, ct);
            foreach (var agentId in agents)
            {
                await RecordAsync(
                    dispatcher,
                    correlation,
                    evt,
                    evt.AgentTenantId,
                    agentId,
                    NotificationKinds.CommunicationSupportMessageReceived,
                    "New message on a support ticket",
                    ct
                );
                // Fila agrupada por ticket: sin `ev`, el clic abre el ticket directo.
                await dispatcher.DispatchAsync(
                    new NotificationIntent(
                        NotificationKinds.CommunicationSupportMessageReceived,
                        evt.AgentTenantId,
                        new NotificationTarget(agentId),
                        evt.EventId,
                        EventType,
                        evt.OccurredOn,
                        correlation.CorrelationId,
                        Push: new PushContent(
                            evt.TicketId.ToString(),
                            Tag: $"support:{evt.TicketId:N}",
                            SkipWhenNoDevices: true
                        )
                    ),
                    ct
                );
            }
        }
    }

    // Asignado → solo él; si no, la lista de Node; si viene vacía (evento viejo), por permiso.
    private static async Task<IReadOnlyList<Guid>> AgentsAsync(
        SupportMessageAddedIntegrationEvent evt,
        IRecipientResolver recipientResolver,
        CancellationToken ct
    )
    {
        IEnumerable<Guid> agents =
            evt.AssignedAgentId is { } assigned && assigned != Guid.Empty ? [assigned]
            : evt.SupportRecipientUserIds.Count > 0 ? evt.SupportRecipientUserIds
            : await recipientResolver.ResolveAsync(
                new ByPermission(evt.AgentTenantId, CommunicationPermissions.SupportAgent),
                ct
            );
        return [.. agents.Where(id => id != Guid.Empty && id != evt.SenderUserId).Distinct()];
    }

    private static Task RecordAsync(
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        SupportMessageAddedIntegrationEvent evt,
        Guid tenantId,
        Guid userId,
        string kind,
        string title,
        CancellationToken ct
    ) =>
        dispatcher.DispatchAsync(
            new NotificationIntent(
                kind,
                tenantId,
                new NotificationTarget(userId),
                evt.EventId,
                EventType,
                evt.OccurredOn,
                correlation.CorrelationId,
                InApp: new InAppContent(
                    ResourceId: evt.TicketId.ToString(),
                    ResourceContext: new Dictionary<string, string>
                    {
                        ["conversationId"] = evt.ConversationId.ToString(),
                    },
                    GroupKey: $"support:{evt.TicketId:N}",
                    FallbackTitle: title
                )
            ),
            ct
        );
}
