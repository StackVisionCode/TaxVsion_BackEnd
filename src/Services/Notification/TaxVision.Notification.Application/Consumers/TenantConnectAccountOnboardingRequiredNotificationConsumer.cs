using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

public static class TenantConnectAccountOnboardingRequiredNotificationConsumer
{
    public static Task Handle(
        TenantConnectAccountOnboardingRequiredIntegrationEvent evt,
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
            NotificationKinds.PaymentClientConnectAccountOnboardingRequired,
            "paymentclient.tenant_connect_account_onboarding_required.v1",
            "Finish setting up your payments account",
            new OfficeAlertEmail(
                "payments.connect_account_onboarding_required.v1",
                new Dictionary<string, object?>(),
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
