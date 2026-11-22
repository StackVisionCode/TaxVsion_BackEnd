using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

internal static class BillingCase
{
    public static TenantSubscriptionEmailRequestedIntegrationEvent Subscription(string status, string reason) =>
        new()
        {
            EventId = DeliveryCase.EventId,
            TenantId = DeliveryCase.TenantId,
            OccurredOn = DeliveryCase.OccurredOn,
            CorrelationId = "corr",
            Email = DeliveryCase.StaffEmail,
            FirstName = "Ana",
            Status = status,
            Reason = reason,
            PlanName = "Enterprise",
            RenewUrl = "https://office.example.com/billing",
            DaysUntilRenewal = 7,
        };

    public static Task LifecycleEmailAsync(
        DeliveryRecorder recorder,
        TenantSubscriptionEmailRequestedIntegrationEvent evt
    ) =>
        SubscriptionLifecycleEmailConsumer.Handle(
            evt,
            recorder.Dispatcher,
            recorder.Scribe,
            DeliveryCase.Portal,
            recorder.Locales,
            new FixedOffices(DeliveryCase.StaffUserId),
            recorder.Directory,
            recorder.Correlation,
            new NoMetrics(),
            new OfficeZoneStub("America/New_York"),
            CancellationToken.None
        );

    public static WalletAlertSender Wallet(DeliveryRecorder recorder) =>
        new(
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId),
            new FixedOffices(DeliveryCase.StaffUserId),
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            DeliveryCase.Host,
            DeliveryCase.Portal,
            recorder.Correlation,
            recorder.Locales
        );

    private sealed class NoMetrics : ISubscriptionEmailMetrics
    {
        public void RecordDunningEmailSent(string type) { }
    }
}

public sealed class BillingAlertConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();
        var resolver = new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId);

        await PayoutFailedNotificationConsumer.Handle(
            new PayoutFailedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
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
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            recorder.Correlation,
            CancellationToken.None
        );
        await TenantConnectAccountRestrictedNotificationConsumer.Handle(
            new TenantConnectAccountRestrictedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TenantConnectAccountId = DeliveryCase.ResourceId,
                RequirementsCurrentlyDue = ["external_account", "tos_acceptance"],
            },
            recorder.Dispatcher,
            resolver,
            recorder.Links,
            recorder.Locales,
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            recorder.Correlation,
            CancellationToken.None
        );
        await TenantConnectAccountOnboardingRequiredNotificationConsumer.Handle(
            new TenantConnectAccountOnboardingRequiredIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TenantConnectAccountId = DeliveryCase.ResourceId,
                RequirementsCurrentlyDue = [],
            },
            recorder.Dispatcher,
            resolver,
            recorder.Links,
            recorder.Locales,
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            recorder.Correlation,
            CancellationToken.None
        );

        // El correo obligatorio es lo único nuevo; la campana no cambió al migrar.
        DeliveryCase.Same(
            [
                "email to=staff@example.com tpl=payments.connect_account_onboarding_required.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=payments.connect_account_restricted.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=payments.payout_failed.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=payments.connect_account_onboarding_required.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=payments.connect_account_restricted.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=payments.payout_failed.v1",
                "inapp kind=payment_client.connect_account_onboarding_required to=staff cat=Billing priority=High title=Finish setting up your payments account body=Review the account status and continue setup. rt=billing rid=resource ctx= params=requirementsCount=0;tenantConnectAccountId=resource action=reconnect src=evt srcType=paymentclient.tenant_connect_account_onboarding_required.v1 group=billing:connect-account:resource actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=payment_client.connect_account_restricted to=staff cat=Billing priority=Urgent title=Payments account restricted body=2 requirements need attention. rt=billing rid=resource ctx= params=requirementsCount=2;tenantConnectAccountId=resource action=reconnect src=evt srcType=paymentclient.tenant_connect_account_restricted.v1 group=billing:connect-account:resource actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=payment_client.payout_failed to=staff cat=Billing priority=Urgent title=Payout failed body=Review the payout details and retry when the issue is resolved. rt=billing rid=resource ctx= params=amount=120.00 USD;currency=usd;payoutScheduleId=resource action=review src=evt srcType=paymentclient.payout_failed.v1 group=billing:payout-failed:resource actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=payments.connect_account_onboarding_required.v1 tpl=payments.connect_account_onboarding_required.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=payments.connect_account_restricted.v1 tpl=payments.connect_account_restricted.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=payments.payout_failed.v1 tpl=payments.payout_failed.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=payment_client.connect_account_onboarding_required tpl=payment_client.connect_account_onboarding_required to=user:staff status=Sent event=evt subject=Finish setting up your payments account",
                "log InApp kind=payment_client.connect_account_restricted tpl=payment_client.connect_account_restricted to=user:staff status=Sent event=evt subject=Payments account restricted",
                "log InApp kind=payment_client.payout_failed tpl=payment_client.payout_failed to=user:staff status=Sent event=evt subject=Payout failed",
                "render payments.connect_account_onboarding_required.v1 audience=Staff vars=action_url=https://office.example.com/settings/billing;language=En",
                "render payments.connect_account_restricted.v1 audience=Staff vars=action_url=https://office.example.com/settings/billing;language=En;requirements_count=2;restricted_at={\"$instant\":\"2026-11-20T15:00:00.0000000Z\"}",
                "render payments.payout_failed.v1 audience=Staff vars=action_url=https://office.example.com/settings/billing;amount_label=120.00 USD;failed_at={\"$instant\":\"2026-11-20T15:00:00.0000000Z\"};failure_reason=account_closed;language=En;payout_reference=po_123",
            ],
            recorder.Lines()
        );
    }
}

public sealed class BillingInAppConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();
        var resolver = new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId);

        await TenantPaymentSucceededNotificationConsumer.Handle(
            new TenantPaymentSucceededIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TenantPaymentId = DeliveryCase.ResourceId,
                ProviderCode = "stripe",
                PurposeKind = "InvoicePayment",
                ExternalReferenceId = DeliveryCase.ResourceId.ToString(),
                AmountCents = 5000,
                Currency = "usd",
                PaidAtUtc = DeliveryCase.OccurredOn,
            },
            recorder.Dispatcher,
            resolver,
            recorder.Correlation,
            CancellationToken.None
        );
        await SaaSPaymentMethodExpiringSoonConsumer.Handle(
            new SaaSPaymentMethodExpiringSoonIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TenantProviderCustomerId = Guid.NewGuid(),
                PaymentMethodId = DeliveryCase.ResourceId,
                Brand = "Visa",
                Last4 = "4242",
                ExpMonth = 1,
                ExpYear = 2027,
                IsDefault = true,
            },
            recorder.Dispatcher,
            resolver,
            recorder.Correlation,
            CancellationToken.None
        );
        await SubscriptionLifecycleInAppConsumer.Handle(
            BillingCase.Subscription("Active", "RenewalUpcoming"),
            recorder.Dispatcher,
            resolver,
            recorder.Correlation,
            new OfficeZoneStub("America/New_York"),
            CancellationToken.None
        );
        await InvoicePaidStaffConsumer.Handle(
            new InvoicePaidIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                InvoiceId = DeliveryCase.ResourceId,
                InvoiceNumber = "INV-7",
                CustomerId = DeliveryCase.CustomerId,
                AmountPaidCents = 5000,
                Currency = "usd",
                PaymentMethod = "card",
                PaidAtUtc = DeliveryCase.OccurredOn,
                CustomerName = "Client Name",
                PaymentSource = "link",
            },
            recorder.Dispatcher,
            resolver,
            recorder.Correlation,
            CancellationToken.None
        );
        await InvoicePaymentRejectedStaffConsumer.Handle(
            new InvoicePaymentRejectedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                UnappliedPaymentId = Guid.NewGuid(),
                TenantPaymentId = Guid.NewGuid(),
                InvoiceId = DeliveryCase.ResourceId,
                ExternalReferenceId = "x",
                AmountCents = 5000,
                Currency = "usd",
                Reason = "InvoiceVoided",
                PaidAtUtc = DeliveryCase.OccurredOn,
            },
            recorder.Dispatcher,
            resolver,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=billing.invoice_paid to=staff cat=Billing priority=Normal title=Invoice INV-7 paid body=50.00 USD received for this invoice. rt=invoice rid=resource ctx=invoiceId=resource params=amount=50.00 USD;customerName=Client Name;invoiceNumber=INV-7;paymentSource=link action=open src=evt srcType=billing.invoice.paid.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=billing.invoice_payment_rejected to=staff cat=Billing priority=High title=Payment of 50.00 USD not applied body=A payment could not be matched to its invoice. Review it in Billing. rt=invoice rid=resource ctx= params=amount=50.00 USD;reason=InvoiceVoided action=review src=evt srcType=billing.invoice.payment_rejected.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=payment_app.payment_method_expiring_soon to=staff cat=Billing priority=High title=Payment method expiring soon body=Visa ending in 4242 expires 01/2027. rt=billing rid=resource ctx= params=brand=Visa;expiry=01/2027;last4=4242;paymentMethodId=resource action=pay src=evt srcType=paymentapp.saas_payment_method_expiring_soon.v1 group=billing:payment-method-expiring:resource actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=payment_client.tenant_payment_succeeded to=staff cat=Billing priority=Normal title=Invoice payment received body=A customer payment of 50.00 USD was confirmed. rt=invoice rid=resource ctx= params=amount=50.00 USD;provider=stripe;tenantPaymentId=resource action=open src=evt srcType=paymentclient.tenant_payment_succeeded.v1 group=billing:invoice-payment:resource actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=subscription.renewal_upcoming to=staff cat=Billing priority=Low title=Subscription renewal coming up body=Your subscription renews in 7 days. rt=billing rid=tenant ctx= params=daysUntilRenewal=7;reason=RenewalUpcoming;status=Active action=open src=evt srcType=auth.tenant_subscription_email_requested.v1 group=billing:subscription:tenant:renewalupcoming actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=billing.invoice_paid tpl=billing.invoice_paid to=user:staff status=Sent event=evt subject=Invoice INV-7 paid",
                "log InApp kind=billing.invoice_payment_rejected tpl=billing.invoice_payment_rejected to=user:staff status=Sent event=evt subject=Payment of 50.00 USD not applied",
                "log InApp kind=payment_app.payment_method_expiring_soon tpl=payment_app.payment_method_expiring_soon to=user:staff status=Sent event=evt subject=Payment method expiring soon",
                "log InApp kind=payment_client.tenant_payment_succeeded tpl=payment_client.tenant_payment_succeeded to=user:staff status=Sent event=evt subject=Invoice payment received",
                "log InApp kind=subscription.renewal_upcoming tpl=subscription.renewal_upcoming to=user:staff status=Sent event=evt subject=Subscription renewal coming up",
            ],
            recorder.Lines()
        );
    }
}

public sealed class WalletAlertConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();
        var topUpId = Guid.Parse("80000000-0000-0000-0000-000000000008");
        recorder.Alias(topUpId, "topup");

        await WalletLowBalanceNotificationConsumer.Handle(
            new WalletLowBalanceIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AvailableMicros = 3_200_000,
                ThresholdMicros = 5_000_000,
                Currency = "USD",
            },
            BillingCase.Wallet(recorder),
            recorder.Correlation,
            CancellationToken.None
        );
        await WalletAutoRechargeSucceededNotificationConsumer.Handle(
            new WalletAutoRechargeSucceededIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TopUpId = topUpId,
                AmountCents = 2000,
                Currency = "USD",
                MethodLabel = "Visa •••• 4242",
                AvailableAfterMicros = 23_200_000,
            },
            BillingCase.Wallet(recorder),
            recorder.Correlation,
            CancellationToken.None
        );
        await WalletAutoRechargePausedNotificationConsumer.Handle(
            new WalletAutoRechargePausedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TopUpId = topUpId,
                Reason = "card_declined",
                AmountCents = 2000,
                Currency = "USD",
                MethodLabel = "Visa •••• 4242",
            },
            BillingCase.Wallet(recorder),
            recorder.Correlation,
            CancellationToken.None
        );
        await WalletAutoRechargeDailyLimitReachedNotificationConsumer.Handle(
            new WalletAutoRechargeDailyLimitReachedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                DailyLimit = 3,
                AmountCents = 2000,
                Currency = "USD",
            },
            BillingCase.Wallet(recorder),
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=staff@example.com tpl=wallet.auto_recharge_daily_limit.v1 event=evt scope=System confidential=False idem=wallet.auto_recharge_daily_limit:evt:staff@example.com attachments=0",
                "email to=staff@example.com tpl=wallet.auto_recharge_paused.v1 event=evt scope=System confidential=False idem=wallet.auto_recharge_paused:topup:staff@example.com attachments=0",
                "email to=staff@example.com tpl=wallet.auto_recharge_succeeded.v1 event=evt scope=System confidential=False idem=wallet.auto_recharge_succeeded:topup:staff@example.com attachments=0",
                "email to=staff@example.com tpl=wallet.low_balance.v1 event=evt scope=System confidential=False idem=wallet.low_balance:evt:staff@example.com attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=wallet.auto_recharge_daily_limit.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=wallet.auto_recharge_paused.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=wallet.auto_recharge_succeeded.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=wallet.low_balance.v1",
                "inapp kind=wallet.auto_recharge_daily_limit to=staff cat=Billing priority=Normal title=Daily automatic recharge limit reached body=Your Wallet reached its limit of 3 automatic recharges today ($20.00 each). It continues tomorrow. rt=wallet_settings rid=tenant ctx= params= action=open src=evt srcType=wallet.auto-recharge.daily-limit.v1 group=wallet:auto-recharge:daily-limit:evt actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=wallet.auto_recharge_paused to=staff cat=Billing priority=High title=Automatic recharge is paused body=We couldn't run your $20.00 automatic recharge with Visa •••• 4242. It won't retry on its own. Review it in Settings → Wallet. rt=wallet_settings rid=topup ctx= params= action=review src=evt srcType=wallet.auto-recharge.paused.v1 group=wallet:auto-recharge:paused:topup actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=wallet.auto_recharge_succeeded to=staff cat=Billing priority=Normal title=Automatic recharge completed body=We added $20.00 to your Wallet using Visa •••• 4242. New balance: $23.20. rt=wallet rid=topup ctx= params= action=open src=evt srcType=wallet.auto-recharge.succeeded.v1 group=wallet:auto-recharge:topup:succeeded actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=wallet.low_balance to=staff cat=Billing priority=High title=Your wallet balance is low body=Your Wallet has $3.20 left, below your $5.00 alert threshold. Add funds to keep messages sending. rt=wallet rid=tenant ctx= params= action=open src=evt srcType=wallet.low-balance.v1 group=wallet:low-balance:tenant actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=wallet.auto_recharge_daily_limit.v1 tpl=wallet.auto_recharge_daily_limit.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=wallet.auto_recharge_paused.v1 tpl=wallet.auto_recharge_paused.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=wallet.auto_recharge_succeeded.v1 tpl=wallet.auto_recharge_succeeded.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=wallet.low_balance.v1 tpl=wallet.low_balance.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=wallet.auto_recharge_daily_limit tpl=wallet.auto_recharge_daily_limit to=user:staff status=Sent event=evt subject=Daily automatic recharge limit reached",
                "log InApp kind=wallet.auto_recharge_paused tpl=wallet.auto_recharge_paused to=user:staff status=Sent event=evt subject=Automatic recharge is paused",
                "log InApp kind=wallet.auto_recharge_succeeded tpl=wallet.auto_recharge_succeeded to=user:staff status=Sent event=evt subject=Automatic recharge completed",
                "log InApp kind=wallet.low_balance tpl=wallet.low_balance to=user:staff status=Sent event=evt subject=Your wallet balance is low",
                "render wallet.auto_recharge_daily_limit.v1 audience=Staff vars=amount_label=$20.00;daily_limit=3;language=En;settings_url=https://office.example.com/settings/wallet",
                "render wallet.auto_recharge_paused.v1 audience=Staff vars=amount_label=$20.00;language=En;method_label=Visa •••• 4242;reason_text=We couldn't run your $20.00 automatic recharge with Visa •••• 4242.;settings_url=https://office.example.com/settings/wallet",
                "render wallet.auto_recharge_succeeded.v1 audience=Staff vars=amount_label=$20.00;balance_after_label=$23.20;charged_at={\"$instant\":\"2026-11-20T15:00:00.0000000Z\"};language=En;method_label=Visa •••• 4242;settings_url=https://office.example.com/settings/wallet;wallet_url=https://office.example.com/wallet",
                "render wallet.low_balance.v1 audience=Staff vars=available_label=$3.20;language=En;settings_url=https://office.example.com/settings/wallet;threshold_label=$5.00;wallet_url=https://office.example.com/wallet",
            ],
            recorder.Lines()
        );
    }
}

public sealed class InvoiceSentConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();
        var pdfId = Guid.Parse("80000000-0000-0000-0000-000000000008");
        recorder.Alias(pdfId, "pdf");

        await InvoiceSentConsumer.Handle(
            new InvoiceSentToCustomerIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                InvoiceId = DeliveryCase.ResourceId,
                InvoiceNumber = "INV-7",
                CustomerEmail = "client@example.com",
                CustomerName = "Client Name",
                AmountDueCents = 5000,
                Currency = "usd",
                DueOn = new DateOnly(2026, 12, 1),
                PaymentLink = "https://pay.example.com/i/7",
                PdfFileId = pdfId,
                CustomerId = DeliveryCase.CustomerId,
            },
            recorder.Scribe,
            AllowAllContactPolicyRepository.Instance,
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(),
            recorder.Locales,
            recorder.Correlation,
            NullLogger<InvoiceSentLog>.Instance,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=client@example.com tpl=billing.invoice_sent event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=client@example.com TemplateKey=billing.invoice_sent",
                "inapp kind=billing.invoice_sent_to_customer to=portal cat=Portal priority=Normal title=New invoice body=Your office sent you an invoice. rt=invoice rid=resource ctx=customerId=customer;invoiceId=resource params=invoiceNumber=INV-7 action=pay src=evt srcType=billing.invoice_sent.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=billing.invoice_sent tpl=billing.invoice_sent to=client@example.com status=Pending event=evt subject=subject",
                "log InApp kind=billing.invoice_sent_to_customer tpl=billing.invoice_sent_to_customer to=user:portal status=Sent event=evt subject=New invoice",
                "render billing.invoice_sent.v1 audience=Client vars=amount_due=50.00 USD;customer_name=Client Name;due_date={\"$date\":\"2026-12-01\"};has_pdf=True;invoice_number=INV-7;language=En;payment_link=https://pay.example.com/i/7",
            ],
            recorder.Lines()
        );
        Assert.Equal([pdfId], Assert.Single(recorder.Email.Requests).AttachmentFileIds);
    }
}

public sealed class SubscriptionEmailConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await BillingCase.LifecycleEmailAsync(recorder, BillingCase.Subscription("Suspended", "PaymentFailed"));
        await BillingCase.LifecycleEmailAsync(recorder, BillingCase.Subscription("Active", "RenewalUpcoming"));
        await SubscriptionDiscountEndingNotificationConsumer.Handle(
            new SubscriptionDiscountEndingIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TenantSubscriptionId = Guid.NewGuid(),
                DiscountId = DeliveryCase.ResourceId,
                EndsAtUtc = DeliveryCase.OccurredOn.AddDays(7),
                DaysUntilEnd = 7,
                Source = "Promotion",
                PercentBasisPoints = 2000,
            },
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId),
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            DeliveryCase.Host,
            DeliveryCase.Portal,
            recorder.Locales,
            recorder.Correlation,
            NullLogger<SubscriptionDiscountEndingIntegrationEvent>.Instance,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=staff@example.com tpl=subscription.discount_ending.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=subscription.renewal_upcoming event=evt scope=System confidential=False idem=tenant:renewal-upcoming::7 attachments=0",
                "email to=staff@example.com tpl=subscription.suspended event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=subscription.discount_ending.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=subscription.renewal_upcoming",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=subscription.suspended",
                "inapp kind=subscription.discount_ending to=staff cat=Billing priority=Low title=Your discount ends soon body=Your 20% discount ends soon. Days left: 7. After that, your plan renews at its regular price. rt=billing rid=resource ctx= params=daysLeft=7;discountId=resource;discountLabel=20% action=open src=evt srcType=SubscriptionDiscountEndingIntegrationEvent group=subscription:discount:resource:ending actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=subscription.discount_ending.v1 tpl=subscription.discount_ending.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=subscription.renewal_upcoming tpl=subscription.renewal_upcoming to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=subscription.suspended tpl=subscription.suspended to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=subscription.discount_ending tpl=subscription.discount_ending to=user:staff status=Sent event=evt subject=Your discount ends soon",
                "render subscription.discount_ending.v1 audience=Staff vars=billing_url=https://office.example.com;days_left=7;discount_label=20%;language=En",
                "render subscription.renewal_upcoming.v1 audience=Staff vars=access_ends_date=;amount=;attempt_number=;cadence=month;card_brand=card;card_last4=----;change_date=;change_summary=;date_charged=;days_until_end=;days_until_renewal=7;discount_amount=;discount_label=;failure_reason=;first_name=Ana;friendly_reason=The payment could not be completed.;has_card=False;is_final=False;item_description=Enterprise;language=En;manage_url=https://office.example.com/billing;max_attempts=;new_mode=;next_charge_date=;next_retry_date=;plan_name=Enterprise;receipt_url=;renew_url=https://office.example.com/billing;renewal_date=;suspension_effect=the service will remain on hold until the payment is completed",
                "render subscription.suspended.v1 audience=Staff vars=access_ends_date=;amount=;attempt_number=;cadence=month;card_brand=card;card_last4=----;change_date=;change_summary=;date_charged=;days_until_end=;days_until_renewal=7;discount_amount=;discount_label=;failure_reason=;first_name=Ana;friendly_reason=The payment could not be completed.;has_card=False;is_final=False;item_description=Enterprise;language=En;manage_url=https://office.example.com/billing;max_attempts=;new_mode=;next_charge_date=;next_retry_date=;plan_name=Enterprise;receipt_url=;renew_url=https://office.example.com/billing;renewal_date=;suspension_effect=the service will remain on hold until the payment is completed",
            ],
            recorder.Lines()
        );
    }
}

/// <summary>Correos al staff que, al pasar por el punto único, respetan la preferencia de correo de facturación.</summary>
public sealed class BillingStaffEmailPreferenceTests
{
    [Fact]
    public async Task Email_off_suppresses_only_the_renewal_reminder()
    {
        var recorder = DeliveryCase.Recorder();
        recorder.Preferences.Off.Add((NotificationCategory.Billing, NotificationChannel.Email));

        await BillingCase.LifecycleEmailAsync(recorder, BillingCase.Subscription("Active", "RenewalUpcoming"));
        await BillingCase.LifecycleEmailAsync(recorder, BillingCase.Subscription("Suspended", "PaymentFailed"));

        // La renovación próxima no es obligatoria: no se encola. La suspensión sí lo es.
        var queued = recorder.Publisher.Published.OfType<NotificationsEmailSendRequestedIntegrationEvent>();
        Assert.Equal(["subscription.suspended"], queued.Select(email => email.TemplateKey).ToArray());
    }
}
