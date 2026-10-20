using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

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

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.CalleeUserId:N}",
                title,
                NotificationCategory.Collaboration,
                "communication.call.missed",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.CalleeUserId,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.CommunicationCallMissed,
                    evt.CalleeUserId,
                    evt.EventId,
                    "communication.call.missed.v1",
                    evt.MissedAtUtc,
                    title,
                    FallbackBody: body,
                    ActorUserId: evt.CallerUserId,
                    ResourceId: evt.CallId.ToString(),
                    ResourceContext: new Dictionary<string, string>
                    {
                        ["callId"] = evt.CallId.ToString(),
                        ["callerUserId"] = evt.CallerUserId.ToString(),
                        ["kind"] = evt.Kind,
                    },
                    GroupKey: $"call:{evt.CallId:N}"
                ),
                ct: ct
            );
        }
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
