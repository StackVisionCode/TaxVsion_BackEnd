using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Directory.Abstractions;
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
    public static Task NotifyInAppAsync(
        ClientFormCustomerNotification notification,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipients,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => RecordPortalAsync(notification, dispatcher, recipients, correlation, ct);

    public static async Task NotifyAsync(
        ClientFormCustomerNotification notification,
        ICustomerEmailDirectoryRepository customers,
        ICustomerContactPolicyRepository contactPolicies,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribe,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipients,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hosts,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var host = await hosts.ResolveHostAsync(notification.TenantId, ct);
        await SendEmailAsync(
            notification,
            host,
            customers,
            contactPolicies,
            gateway,
            scribe,
            portal.Value,
            correlation,
            ct
        );
        await RecordPortalAsync(notification, dispatcher, recipients, correlation, ct);
    }

    private static async Task SendEmailAsync(
        ClientFormCustomerNotification notification,
        string? host,
        ICustomerEmailDirectoryRepository customers,
        ICustomerContactPolicyRepository contactPolicies,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribe,
        PortalOptions portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (
            !await CustomerContactPolicyFilter.AllowsAsync(
                notification.TenantId,
                notification.CustomerId,
                CustomerContactChannel.Email,
                contactPolicies,
                ct
            )
        )
            return;
        var customer = await customers.GetByCustomerIdAsync(notification.TenantId, notification.CustomerId, ct);
        if (customer is not { IsActive: true } || customer.NormalizedEmail.Length == 0)
            return;

        var variables = new Dictionary<string, object?>(notification.EmailVariables)
        {
            ["customer_name"] = customer.DisplayName,
            ["portal_link"] = TenantEmailLinks.ClientForm(host, portal, notification.FormRequestId),
        };
        var render = (
            await scribe.RenderAsync(notification.TemplateKey, notification.TenantId, variables, ct)
        ).EnsureRendered(notification.TemplateKey);
        await gateway.QueueEmailAsync(
            render.ToDispatchRequest(
                notification.TenantId,
                customer.NormalizedEmail,
                notification.TemplateKey,
                notification.EventId,
                correlation.CorrelationId
            ),
            ct
        );
    }

    private static async Task RecordPortalAsync(
        ClientFormCustomerNotification notification,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipients,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var userId = await CustomerPortalRecipients.ResolveActivePortalUserAsync(
            notification.TenantId,
            notification.CustomerId,
            recipients,
            ct
        );
        if (!userId.HasValue)
            return;
        await dispatcher.RecordInAppAsync(
            notification.TenantId,
            $"user:{userId.Value:N}",
            notification.Title,
            NotificationCategory.ClientRequests,
            notification.Kind,
            notification.EventId,
            correlation.CorrelationId,
            recipientUserId: userId.Value,
            inApp: new InAppNotificationRequest(
                notification.Kind,
                userId.Value,
                notification.EventId,
                notification.TemplateKey,
                notification.OccurredOn,
                notification.Title,
                FallbackBody: notification.Body,
                ResourceId: notification.FormRequestId.ToString(),
                ResourceContext: new Dictionary<string, string>
                {
                    ["customerId"] = notification.CustomerId.ToString(),
                    ["formRequestId"] = notification.FormRequestId.ToString(),
                },
                Params: notification.Params,
                GroupKey: $"form_request:{notification.FormRequestId:N}"
            ),
            ct: ct
        );
    }
}
