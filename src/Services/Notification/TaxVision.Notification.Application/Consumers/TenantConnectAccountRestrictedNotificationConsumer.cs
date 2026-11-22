using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

public static class TenantConnectAccountRestrictedNotificationConsumer
{
    public static Task Handle(
        TenantConnectAccountRestrictedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        PushLinks links,
        IRecipientLocale locales,
        IUserEmailResolver emails,
        IScribeRenderClient scribe,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        TenantConnectAccountNotificationPublisher.RecordAsync(
            evt.TenantId,
            evt.EventId,
            evt.CorrelationId,
            evt.OccurredOn,
            evt.TenantConnectAccountId,
            evt.RequirementsCurrentlyDue.Count,
            NotificationKinds.PaymentClientConnectAccountRestricted,
            "paymentclient.tenant_connect_account_restricted.v1",
            "Payments account restricted",
            new OfficeAlertEmail(
                "payments.connect_account_restricted.v1",
                new Dictionary<string, object?> { ["restricted_at"] = ScribeDates.Instant(evt.OccurredOn) },
                links,
                locales,
                emails,
                scribe
            ),
            dispatcher,
            resolver,
            correlation,
            ct
        );
}
