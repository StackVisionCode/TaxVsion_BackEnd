using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

/// <summary>
/// Avisos obligatorios de cobros en línea: el correo sale con su plantilla propia, en el idioma de quien lee, aunque
/// la persona tenga apagado el correo de facturación.
/// </summary>
public sealed class PaymentsMandatoryEmailTests
{
    private const string BillingUrl = "action_url=https://office.example.com/settings/billing";
    private const string At = "{\"$instant\":\"2026-11-20T15:00:00.0000000Z\"}";

    public static TheoryData<string> Kinds =>
        new()
        {
            NotificationKinds.PaymentClientPayoutFailed,
            NotificationKinds.PaymentClientConnectAccountRestricted,
            NotificationKinds.PaymentClientConnectAccountOnboardingRequired,
        };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Mandatory_email_is_sent_with_the_new_template(string kind)
    {
        var recorder = DeliveryCase.Recorder("es");
        recorder.Preferences.Off.Add((NotificationCategory.Billing, NotificationChannel.Email));

        var (template, vars) = await HandleAsync(kind, recorder);

        var lines = recorder.Lines();
        Assert.Contains($"render {template} audience=Staff vars={vars}", lines);
        Assert.Contains(
            $"log Email kind={template} tpl={template} to={DeliveryCase.StaffEmail} status=Pending event=evt subject=subject",
            lines
        );
        Assert.Contains(
            $"event NotificationsEmailSendRequestedIntegrationEvent To={DeliveryCase.StaffEmail} TemplateKey={template}",
            lines
        );
        var request = Assert.Single(recorder.Email.Requests);
        Assert.Equal(DeliveryCase.StaffUserId, request.RecipientUserId);
        Assert.Equal(NotificationCategory.Billing, request.Category);
        Assert.Equal(kind, request.NotificationKind);
    }

    private static async Task<(string Template, string Vars)> HandleAsync(string kind, DeliveryRecorder recorder)
    {
        var resolver = new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId);
        var emails = DeliveryCase.Emails(recorder);
        switch (kind)
        {
            case NotificationKinds.PaymentClientPayoutFailed:
                await PayoutFailedNotificationConsumer.Handle(
                    new PayoutFailedIntegrationEvent
                    {
                        EventId = DeliveryCase.EventId,
                        TenantId = DeliveryCase.TenantId,
                        OccurredOn = DeliveryCase.OccurredOn,
                        PayoutScheduleId = DeliveryCase.ResourceId,
                        ProviderPayoutReference = "po_123",
                        AmountCents = 12000,
                        Currency = "usd",
                        FailureReason = "account_closed",
                        FailedAtUtc = DeliveryCase.OccurredOn,
                    },
                    recorder.Dispatcher,
                    resolver,
                    recorder.Links,
                    recorder.Locales,
                    emails,
                    recorder.Scribe,
                    recorder.Correlation,
                    CancellationToken.None
                );
                return (
                    "payments.payout_failed.v1",
                    $"{BillingUrl};amount_label=120.00 USD;failed_at={At};failure_reason=account_closed;language=Es;payout_reference=po_123"
                );
            case NotificationKinds.PaymentClientConnectAccountRestricted:
                await TenantConnectAccountRestrictedNotificationConsumer.Handle(
                    new TenantConnectAccountRestrictedIntegrationEvent
                    {
                        EventId = DeliveryCase.EventId,
                        TenantId = DeliveryCase.TenantId,
                        OccurredOn = DeliveryCase.OccurredOn,
                        TenantConnectAccountId = DeliveryCase.ResourceId,
                        RequirementsCurrentlyDue = ["external_account", "tos_acceptance"],
                    },
                    recorder.Dispatcher,
                    resolver,
                    recorder.Links,
                    recorder.Locales,
                    emails,
                    recorder.Scribe,
                    recorder.Correlation,
                    CancellationToken.None
                );
                return (
                    "payments.connect_account_restricted.v1",
                    $"{BillingUrl};language=Es;requirements_count=2;restricted_at={At}"
                );
            default:
                // Sin requisitos pendientes la variable no viaja (la fila no aparece en el correo).
                await TenantConnectAccountOnboardingRequiredNotificationConsumer.Handle(
                    new TenantConnectAccountOnboardingRequiredIntegrationEvent
                    {
                        EventId = DeliveryCase.EventId,
                        TenantId = DeliveryCase.TenantId,
                        OccurredOn = DeliveryCase.OccurredOn,
                        TenantConnectAccountId = DeliveryCase.ResourceId,
                        RequirementsCurrentlyDue = [],
                    },
                    recorder.Dispatcher,
                    resolver,
                    recorder.Links,
                    recorder.Locales,
                    emails,
                    recorder.Scribe,
                    recorder.Correlation,
                    CancellationToken.None
                );
                return ("payments.connect_account_onboarding_required.v1", $"{BillingUrl};language=Es");
        }
    }
}
