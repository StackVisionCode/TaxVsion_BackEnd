using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Communication;

public static class CallMissedConsumer
{
    public static async Task Handle(
        CallMissedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (!await CanReceiveCallsAsync(evt.TenantId, evt.CalleeUserId, resolver, ct))
                return;

            const string title = "Missed call";
            var body = string.Equals(evt.Kind, "Video", StringComparison.OrdinalIgnoreCase)
                ? "You missed a video call."
                : "You missed an audio call.";

            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.CommunicationCallMissed,
                    evt.TenantId,
                    new NotificationTarget(evt.CalleeUserId),
                    evt.EventId,
                    "communication.call.missed.v1",
                    evt.MissedAtUtc,
                    correlation.CorrelationId,
                    InApp: new InAppContent(
                        Params: new Dictionary<string, string> { ["kind"] = evt.Kind },
                        // La fila abre la conversación; sin ella (evento viejo) el front decide el destino.
                        ResourceId: evt.ConversationId?.ToString(),
                        ResourceContext: Context(evt),
                        // Una fila por contacto: Node suma el contador mientras no se lea.
                        GroupKey: $"call-missed:{evt.CallerUserId:N}",
                        ActorUserId: evt.CallerUserId,
                        FallbackTitle: title,
                        FallbackBody: body
                    )
                ),
                ct
            );
        }
    }

    private static Dictionary<string, string> Context(CallMissedIntegrationEvent evt)
    {
        var context = new Dictionary<string, string>
        {
            ["callId"] = evt.CallId.ToString(),
            ["callerUserId"] = evt.CallerUserId.ToString(),
            ["kind"] = evt.Kind,
        };
        if (evt.ConversationId is { } conversationId)
            context["conversationId"] = conversationId.ToString();
        return context;
    }

    private static async Task<bool> CanReceiveCallsAsync(
        Guid tenantId,
        Guid userId,
        IRecipientResolver resolver,
        CancellationToken ct
    )
    {
        var staff = await resolver.ResolveAsync(
            new ByPermission(tenantId, CommunicationPermissions.CallStart),
            new RecipientValidation(
                RequiredCapability: CommunicationPermissions.CallStart,
                RequiredModule: PermissionModuleMap.ModuleFor(CommunicationPermissions.CallStart)
            ),
            ct
        );
        if (staff.IsSuccess && staff.Value.Contains(userId))
            return true;

        var portal = await resolver.ResolveAsync(
            new ByPermission(tenantId, PortalPermissions.CallsUse),
            new RecipientValidation(RequiredCapability: PortalPermissions.CallsUse),
            ct
        );
        return portal.IsSuccess && portal.Value.Contains(userId);
    }
}
