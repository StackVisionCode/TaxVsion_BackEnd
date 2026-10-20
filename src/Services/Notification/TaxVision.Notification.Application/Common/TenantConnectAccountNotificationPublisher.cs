using System.Globalization;
using BuildingBlocks.Common;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;

namespace TaxVision.Notification.Application.Common;

internal static class TenantConnectAccountNotificationPublisher
{
    public static async Task RecordAsync(
        Guid tenantId,
        Guid eventId,
        string correlationId,
        DateTime occurredOn,
        Guid accountId,
        int requirementsCount,
        string kind,
        string sourceEventType,
        string title,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(correlationId, eventId)))
        {
            var recipients = await BillingNotificationRecipients.ResolveOwnerAndSubscribersAsync(
                tenantId,
                resolver,
                ct
            );
            await BillingNotificationPublisher.RecordAsync(
                dispatcher,
                recipients,
                tenantId,
                eventId,
                correlation.CorrelationId,
                kind,
                sourceEventType,
                occurredOn,
                title,
                requirementsCount > 0
                    ? $"{requirementsCount} requirement{(requirementsCount == 1 ? string.Empty : "s")} need attention."
                    : "Review the account status and continue setup.",
                accountId.ToString(),
                new Dictionary<string, string>
                {
                    ["tenantConnectAccountId"] = accountId.ToString(),
                    ["requirementsCount"] = requirementsCount.ToString(CultureInfo.InvariantCulture),
                },
                $"billing:connect-account:{accountId:N}",
                ct
            );
        }
    }
}
