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
        OfficeAlertEmail email,
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
            if (recipients.Count == 0)
                return;

            // Correo obligatorio antes de la campana; sin requisitos pendientes la fila no aparece.
            var variables = new Dictionary<string, object?>(email.Variables)
            {
                ["action_url"] = await OfficeAlertEmails.BillingUrlAsync(email.Links, tenantId, ct),
            };
            if (requirementsCount > 0)
                variables["requirements_count"] = requirementsCount;
            await OfficeAlertEmails.SendAsync(
                new NotificationIntent(
                    kind,
                    tenantId,
                    new NotificationTarget(),
                    eventId,
                    sourceEventType,
                    occurredOn,
                    correlation.CorrelationId
                ),
                email.EventKey,
                variables,
                recipients,
                dispatcher,
                email.Emails,
                email.Scribe,
                email.Locales,
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

/// <summary>Plantilla del correo obligatorio y lo que hace falta para mandarlo.</summary>
internal sealed record OfficeAlertEmail(
    string EventKey,
    IReadOnlyDictionary<string, object?> Variables,
    PushLinks Links,
    IRecipientLocale Locales,
    IUserEmailResolver Emails,
    IScribeRenderClient Scribe
);
