using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

internal sealed record ClientFormCustomerNotification(
    Guid TenantId,
    Guid CustomerId,
    Guid FormRequestId,
    Guid EventId,
    DateTime OccurredOn,
    string Kind,
    string TemplateKey,
    string Title,
    string Body,
    IReadOnlyDictionary<string, object?> EmailVariables,
    IReadOnlyDictionary<string, string> Params
);

internal static class ClientFormCustomerNotifier
{
    public static async Task NotifyAsync(
        ClientFormCustomerNotification notification,
        ClientFormNotificationSender sender,
        IRecipientResolver recipients,
        CancellationToken ct
    )
    {
        var userId = await CustomerPortalRecipients.ResolveActivePortalUserAsync(
            notification.TenantId,
            notification.CustomerId,
            recipients,
            ct
        );
        await sender.SendAsync(
            new ClientFormNotificationRequest(
                notification.TenantId,
                userId,
                notification.FormRequestId,
                notification.EventId,
                notification.OccurredOn,
                notification.Kind,
                notification.TemplateKey,
                PushTemplateKey(notification.TemplateKey),
                notification.Title,
                notification.Body,
                notification.EmailVariables,
                notification.Params,
                NotificationCategory.ClientRequests,
                notification.CustomerId,
                CustomerRecipient: true
            ),
            ct
        );
    }

    private static string PushTemplateKey(string emailTemplateKey) =>
        emailTemplateKey.EndsWith(".v1", StringComparison.Ordinal)
            ? $"{emailTemplateKey[..^3]}.push.v1"
            : emailTemplateKey;
}
