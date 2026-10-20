using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Notifies the audience for an incoming customer email.
/// </summary>
public static class CorrespondenceCustomerEmailReceivedConsumer
{
    private const string EmailModule = "email";

    public static async Task Handle(
        CorrespondenceCustomerEmailReceivedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.AccountId is not { } accountId)
                return;

            // Capacidad por tipo de buzon: personal exige connectors.accounts.read; oficina
            // exige connectors.accounts.office.read. En ambos casos el modulo email debe estar
            // habilitado para el tenant. Fail-closed: sin la capacidad no se recibe el aviso.
            var requiredCapability = evt.MailboxOwnerUserId is null
                ? ConnectorsPermissions.AccountsOfficeRead
                : ConnectorsPermissions.AccountsRead;

            var resolved = await recipientResolver.ResolveAsync(
                new MailboxAudience(evt.TenantId, accountId, evt.MailboxOwnerUserId, evt.CustomerId),
                new RecipientValidation(RequiredCapability: requiredCapability, RequiredModule: EmailModule),
                ct
            );
            if (resolved.IsFailure)
                return;

            var title = "Email from a client";
            var groupKey = $"email_thread:{evt.EmailThreadId}";
            var resourceContext = new Dictionary<string, string>
            {
                ["accountId"] = accountId.ToString(),
                ["customerId"] = evt.CustomerId.ToString(),
                ["messageId"] = evt.IncomingEmailId.ToString(),
            };

            foreach (var userId in resolved.Value)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    $"user:{userId:N}",
                    title,
                    NotificationCategory.Collaboration,
                    "correspondence.customer_email_received",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.CorrespondenceCustomerEmailReceived,
                        userId,
                        evt.EventId,
                        "correspondence.customer_email_received.v1",
                        evt.OccurredOn,
                        title,
                        // El asunto puede contener datos del cliente: no se filtra en el fallback
                        // de la bandeja. El front abre el hilo para mostrarlo con el permiso real.
                        FallbackBody: "A new email arrived from a client.",
                        ResourceId: evt.EmailThreadId.ToString(),
                        ResourceContext: resourceContext,
                        GroupKey: groupKey
                    ),
                    ct: ct
                );
            }
        }
    }
}
