using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

public sealed class BillingAlertConsumersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Expiring_payment_method_notifies_owner_and_subscribers_once()
    {
        var owner = Guid.NewGuid();
        var subscriber = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new AudienceResolver(owner, owner, subscriber);

        await SaaSPaymentMethodExpiringSoonConsumer.Handle(
            new SaaSPaymentMethodExpiringSoonIntegrationEvent
            {
                TenantId = TenantId,
                TenantProviderCustomerId = Guid.NewGuid(),
                PaymentMethodId = Guid.NewGuid(),
                Brand = "Visa",
                Last4 = "4242",
                ExpMonth = 12,
                ExpYear = 2026,
                IsDefault = true,
            },
            harness.Dispatcher,
            resolver,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        var events = harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().ToArray();
        Assert.Equal(2, events.Length);
        Assert.Equal(
            new[] { owner, subscriber }.Order().ToArray(),
            events.Select(x => x.RecipientUserId).Order().ToArray()
        );
        Assert.All(events, x => Assert.Equal(NotificationKinds.PaymentAppPaymentMethodExpiringSoon, x.Kind));
    }

    [Fact]
    public async Task Tenant_payment_notifies_only_billing_subscribers_for_invoice_payments()
    {
        var owner = Guid.NewGuid();
        var subscriber = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new AudienceResolver(owner, subscriber);
        var invoiceId = Guid.NewGuid();

        await TenantPaymentSucceededNotificationConsumer.Handle(
            new TenantPaymentSucceededIntegrationEvent
            {
                TenantId = TenantId,
                TenantPaymentId = Guid.NewGuid(),
                ProviderCode = "Stripe",
                PurposeKind = "InvoicePayment",
                ExternalReferenceId = invoiceId.ToString(),
                AmountCents = 12500,
                Currency = "USD",
                PaidAtUtc = DateTime.UtcNow,
            },
            harness.Dispatcher,
            resolver,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        var evt = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(harness.Publisher.Published));
        Assert.Equal(subscriber, evt.RecipientUserId);
        Assert.Equal(invoiceId.ToString(), evt.ResourceId);
    }

    [Fact]
    public async Task Tenant_payment_ignores_non_invoice_purposes()
    {
        var harness = new NotificationDispatcherHarness();

        await TenantPaymentSucceededNotificationConsumer.Handle(
            new TenantPaymentSucceededIntegrationEvent
            {
                TenantId = TenantId,
                TenantPaymentId = Guid.NewGuid(),
                ProviderCode = "Stripe",
                PurposeKind = "PaymentLink",
                AmountCents = 1000,
                Currency = "USD",
                PaidAtUtc = DateTime.UtcNow,
            },
            harness.Dispatcher,
            new AudienceResolver(Guid.NewGuid(), Guid.NewGuid()),
            new TestCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(harness.Publisher.Published);
    }

    [Theory]
    [InlineData(false, "Payment attempt failed", "We'll try again on October 23.")]
    [InlineData(true, "Payment failed — action required", "Complete the payment in Manage subscription")]
    public async Task A_failed_charge_attempt_shows_what_happens_next_in_the_bell(
        bool isFinal,
        string expectedTitle,
        string expectedBodyPart
    )
    {
        var harness = new NotificationDispatcherHarness();

        await SubscriptionLifecycleInAppConsumer.Handle(
            new TenantSubscriptionEmailRequestedIntegrationEvent
            {
                TenantId = TenantId,
                Email = "owner@example.com",
                FirstName = "Owner",
                Status = isFinal ? "Suspended" : "Active",
                Reason = "RenewalChargeFailed",
                RenewUrl = "https://www.taxoffix.io/account/plan",
                Amount = "$49.00",
                ItemDescription = "Professional plan",
                IsFinalAttempt = isFinal,
                NextAttemptAtUtc = isFinal ? null : new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc),
            },
            harness.Dispatcher,
            new AudienceResolver(Guid.NewGuid()),
            new TestCorrelationContext(),
            CancellationToken.None
        );

        var evt = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(harness.Publisher.Published));
        Assert.Equal(expectedTitle, evt.FallbackTitle);
        Assert.Contains("We couldn't charge $49.00 for your Professional plan.", evt.FallbackBody);
        Assert.Contains(expectedBodyPart, evt.FallbackBody);
    }

    [Fact]
    public async Task Renewal_upcoming_uses_renewal_kind()
    {
        var harness = new NotificationDispatcherHarness();
        var owner = Guid.NewGuid();

        await SubscriptionLifecycleInAppConsumer.Handle(
            new TenantSubscriptionEmailRequestedIntegrationEvent
            {
                TenantId = TenantId,
                Email = "owner@example.com",
                FirstName = "Owner",
                Status = "Active",
                Reason = "RenewalUpcoming",
                DaysUntilRenewal = 7,
                RenewUrl = "https://office.example.com",
            },
            harness.Dispatcher,
            new AudienceResolver(owner),
            new TestCorrelationContext(),
            CancellationToken.None
        );

        var evt = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(harness.Publisher.Published));
        Assert.Equal(NotificationKinds.SubscriptionRenewalUpcoming, evt.Kind);
        Assert.Equal(owner, evt.RecipientUserId);
    }

    [Fact]
    public async Task Missed_call_notifies_only_authorized_callee()
    {
        var caller = Guid.NewGuid();
        var callee = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new AudienceResolver(Guid.NewGuid(), callRecipients: [callee]);

        await CallMissedConsumer.Handle(
            new CallMissedIntegrationEvent
            {
                TenantId = TenantId,
                CallId = Guid.NewGuid(),
                CallerUserId = caller,
                CalleeUserId = callee,
                Kind = "Audio",
                RingingAtUtc = DateTime.UtcNow.AddSeconds(-30),
                MissedAtUtc = DateTime.UtcNow,
            },
            harness.Dispatcher,
            resolver,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        var evt = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(harness.Publisher.Published));
        Assert.Equal(callee, evt.RecipientUserId);
        Assert.Equal(caller, evt.ActorUserId);
        Assert.Equal(NotificationKinds.CommunicationCallMissed, evt.Kind);
    }

    private sealed class AudienceResolver(Guid owner, params Guid[] billingSubscribers) : IRecipientResolver
    {
        private readonly IReadOnlyList<Guid> callRecipients = [];

        public AudienceResolver(Guid owner, Guid[] callRecipients, params Guid[] billingSubscribers)
            : this(owner, billingSubscribers)
        {
            this.callRecipients = callRecipients;
        }

        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult(ResolvePermission(audience.PermissionCode));

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Success<IReadOnlyList<Guid>>(
                    audience switch
                    {
                        TenantOwnerAudience => [owner],
                        ByPermission permission => ResolvePermission(permission.PermissionCode),
                        _ => [],
                    }
                )
            );

        private IReadOnlyList<Guid> ResolvePermission(string permissionCode) =>
            permissionCode switch
            {
                NotificationPermissions.BillingAlerts => billingSubscribers,
                CommunicationPermissions.CallStart or PortalPermissions.CallsUse => callRecipients,
                _ => [],
            };
    }

    private sealed class TestCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
