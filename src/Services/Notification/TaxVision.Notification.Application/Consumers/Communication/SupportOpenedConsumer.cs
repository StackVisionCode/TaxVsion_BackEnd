using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// <c>communication.support.opened.v1</c> - a tenant opened a support ticket that the platform
/// support queue must see. The ticket belongs to the office tenant, but agent devices are registered
/// under the platform tenant, so fan-out must resolve recipients against <see cref="SupportOpenedIntegrationEvent.AgentTenantId"/>.
/// </summary>
public static class SupportOpenedConsumer
{
    private const string TemplateKey = "communication.support.opened";

    public static async Task Handle(
        SupportOpenedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var recipients =
                evt.SupportRecipientUserIds.Count > 0
                    ? evt.SupportRecipientUserIds.Distinct().ToArray()
                    : await recipientResolver.ResolveAsync(
                        new ByPermission(evt.AgentTenantId, CommunicationPermissions.SupportAgent),
                        ct
                    );

            if (recipients.Count == 0)
                return;

            var title = $"New support ticket: {evt.Subject}";
            var body = BuildBody(evt);

            foreach (var userId in recipients)
            {
                await dispatcher.RecordInAppAsync(
                    evt.AgentTenantId,
                    userId.ToString("N"),
                    title,
                    NotificationCategory.Collaboration,
                    TemplateKey,
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    ct: ct
                );

                await dispatcher.SendPushAsync(
                    evt.AgentTenantId,
                    userId,
                    title,
                    body,
                    NotificationCategory.Collaboration,
                    TemplateKey,
                    evt.EventId,
                    correlation.CorrelationId,
                    ct
                );
            }
        }
    }

    private static string BuildBody(SupportOpenedIntegrationEvent evt)
    {
        var priority = string.IsNullOrWhiteSpace(evt.Priority) ? "Normal" : evt.Priority.Trim();
        var category = string.IsNullOrWhiteSpace(evt.Category) ? "Other" : evt.Category.Trim();
        return $"{priority} priority - {category}. Open the support console to claim it.";
    }
}
