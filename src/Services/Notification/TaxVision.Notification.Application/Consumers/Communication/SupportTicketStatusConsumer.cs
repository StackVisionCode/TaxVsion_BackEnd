using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Reclamado, resuelto y cerrado: aviso in-app solo a quien abriÃ³ el ticket, en su oficina.
/// Sin asunto ni nombre del agente. No va detrÃ¡s del flag Support: Node no produce estos kinds.
/// </summary>
public static class SupportTicketStatusConsumer
{
    public static Task Handle(
        SupportClaimedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        ILogger<SupportClaimedIntegrationEvent> logger,
        CancellationToken ct
    ) =>
        NotifyOpenerAsync(
            new StatusNotice(
                evt,
                evt.TicketId,
                evt.OpenedByUserId,
                evt.AssignedAgentId,
                NotificationKinds.CommunicationSupportClaimed,
                "communication.support.claimed.v1",
                "Support is working on your ticket",
                null
            ),
            dispatcher,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        SupportResolvedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        ILogger<SupportResolvedIntegrationEvent> logger,
        CancellationToken ct
    ) =>
        NotifyOpenerAsync(
            new StatusNotice(
                evt,
                evt.TicketId,
                evt.OpenedByUserId,
                evt.ResolvedByUserId,
                NotificationKinds.CommunicationSupportResolved,
                "communication.support.resolved.v1",
                "Your support ticket was resolved",
                "Reopen it if you still need help."
            ),
            dispatcher,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        SupportClosedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        ILogger<SupportClosedIntegrationEvent> logger,
        CancellationToken ct
    ) =>
        NotifyOpenerAsync(
            new StatusNotice(
                evt,
                evt.TicketId,
                evt.OpenedByUserId,
                evt.ClosedByUserId,
                NotificationKinds.CommunicationSupportClosed,
                "communication.support.closed.v1",
                "Your support ticket was closed",
                null
            ),
            dispatcher,
            correlation,
            logger,
            ct
        );

    private sealed record StatusNotice(
        IntegrationEvent Event,
        Guid TicketId,
        Guid? OpenedByUserId,
        Guid ActorUserId,
        string Kind,
        string EventType,
        string Title,
        string? Body
    );

    private static async Task NotifyOpenerAsync(
        StatusNotice notice,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        var evt = notice.Event;
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.TenantId == Guid.Empty)
                return;
            if (notice.OpenedByUserId is not { } openerId || openerId == Guid.Empty)
            {
                logger.LogWarning("Support ticket {TicketId} event has no opener; nobody notified", notice.TicketId);
                return;
            }
            // Nadie recibe aviso de su propio acto.
            if (openerId == notice.ActorUserId)
                return;

            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    notice.Kind,
                    evt.TenantId,
                    new NotificationTarget(openerId),
                    evt.EventId,
                    notice.EventType,
                    evt.OccurredOn,
                    correlation.CorrelationId,
                    InApp: new InAppContent(
                        ResourceId: notice.TicketId.ToString(),
                        FallbackTitle: notice.Title,
                        FallbackBody: notice.Body
                    )
                ),
                ct
            );
        }
    }
}
