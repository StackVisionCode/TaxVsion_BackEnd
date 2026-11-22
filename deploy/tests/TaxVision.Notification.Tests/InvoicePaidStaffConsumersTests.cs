using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Avisos de campana AL PERSONAL sobre facturas: "factura pagada" y "cobro sin aplicar". Los recibe el dueño de la
/// oficina y quien se suscribió a las alertas de facturación, siempre que pueda ver facturas. Nunca el cliente ni otra
/// oficina. La reentrega del mismo evento conserva su id: Communication actualiza la fila en vez de duplicarla.
/// </summary>
public sealed class InvoicePaidStaffConsumersTests
{
    private const string InvoicesView = "invoicing.view";

    private static readonly Guid Office = Guid.Parse("20000000-0000-0000-0000-000000000007");
    private static readonly Guid OtherOffice = Guid.Parse("20000000-0000-0000-0000-000000000008");
    private static readonly Guid Owner = Guid.Parse("11111111-1111-4111-8111-111111111117");
    private static readonly Guid Subscriber = Guid.Parse("22222222-2222-4222-8222-222222222227");
    private static readonly Guid SubscriberWithoutInvoices = Guid.Parse("33333333-3333-4333-8333-333333333337");
    private static readonly Guid EmployeeNotSubscribed = Guid.Parse("44444444-4444-4444-8444-444444444447");
    private static readonly Guid SubscriberOfOtherOffice = Guid.Parse("55555555-5555-4555-8555-555555555557");
    private static readonly Guid InvoiceId = Guid.Parse("70000000-0000-0000-0000-000000000007");
    private static readonly Guid UnappliedPaymentId = Guid.Parse("80000000-0000-0000-0000-000000000007");
    private static readonly DateTime PaidAt = new(2026, 11, 20, 15, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Paid_invoice_rings_the_owner_and_the_billing_subscribers_once_each()
    {
        var h = new Harness(WholeOffice());

        await h.PaidAsync(Paid());

        // El dueño también está suscrito: recibe uno solo.
        Assert.Equal(new[] { Owner, Subscriber }.Order(), h.InApp.Select(row => row.RecipientUserId).Order());
        Assert.All(
            h.InApp,
            row =>
            {
                Assert.Equal(NotificationKinds.BillingInvoicePaid, row.Kind);
                Assert.Equal(Office, row.TenantId);
                Assert.Equal("Billing", row.Category);
                Assert.Equal("invoice", row.ResourceType);
                Assert.Equal(InvoiceId.ToString(), row.ResourceId);
                Assert.Equal(InvoiceId.ToString(), row.ResourceContext["invoiceId"]);
            }
        );
    }

    [Fact]
    public async Task Paid_invoice_carries_the_number_the_amount_and_the_customer()
    {
        var h = new Harness(WholeOffice());

        await h.PaidAsync(Paid());

        var row = h.InApp[0];
        Assert.Equal("INV-2026-00007", row.Params["invoiceNumber"]);
        Assert.Equal("125.00 USD", row.Params["amount"]);
        Assert.Equal("Jane Client", row.Params["customerName"]);
    }

    [Fact]
    public async Task Fallback_comes_from_the_copy_catalog()
    {
        var h = new Harness(WholeOffice());

        await h.PaidAsync(Paid());

        var row = h.InApp[0];
        Assert.Equal("Invoice INV-2026-00007 paid", row.FallbackTitle);
        Assert.Equal("125.00 USD received for this invoice.", row.FallbackBody);
    }

    [Fact]
    public async Task Paid_invoice_without_a_customer_name_is_still_announced()
    {
        var h = new Harness(WholeOffice());

        await h.PaidAsync(Paid() with { CustomerName = null });

        var row = h.InApp[0];
        Assert.Equal(string.Empty, row.Params["customerName"]);
        Assert.Contains("INV-2026-00007", row.FallbackTitle);
        Assert.Contains("125.00 USD", row.FallbackBody);
    }

    [Fact]
    public async Task The_paid_notice_carries_how_it_was_paid()
    {
        // El texto por origen lo decide el archivo de textos; aquí solo viaja el dato.
        Assert.Equal("Online", await PaymentSourceOfPaidAsync("Online"));
        Assert.Equal("Manual", await PaymentSourceOfPaidAsync("Manual"));
        Assert.Equal(string.Empty, await PaymentSourceOfPaidAsync(null));
    }

    [Fact]
    public async Task Paid_invoice_is_not_told_to_staff_without_invoices_or_without_the_subscription()
    {
        var h = new Harness(WholeOffice());

        await h.PaidAsync(Paid());

        var told = h.InApp.Select(row => row.RecipientUserId).ToArray();
        Assert.DoesNotContain(SubscriberWithoutInvoices, told);
        Assert.DoesNotContain(EmployeeNotSubscribed, told);
        Assert.DoesNotContain(SubscriberOfOtherOffice, told);
        h.AssertAskedOnlyForOwnerAndBillingSubscribersWhoSeeInvoices();
    }

    [Fact]
    public async Task Paid_invoice_with_nobody_to_tell_publishes_nothing()
    {
        var h = new Harness(new Person(SubscriberWithoutInvoices, Office, BillingAlerts: true, CanViewInvoices: false));

        await h.PaidAsync(Paid());

        Assert.Empty(h.InApp);
    }

    [Fact]
    public async Task A_redelivered_paid_invoice_keeps_the_same_event_for_each_person()
    {
        var h = new Harness(WholeOffice());
        var evt = Paid();

        await h.PaidAsync(evt);
        await h.PaidAsync(evt);

        // Se republica con el mismo evento de origen: Communication actualiza la fila sin duplicarla.
        Assert.Equal(4, h.InApp.Count);
        Assert.Equal(2, h.InApp.Select(row => (row.RecipientUserId, row.SourceEventId, row.Kind)).Distinct().Count());
        Assert.All(
            h.InApp,
            row =>
            {
                Assert.Equal(evt.EventId, row.SourceEventId);
                Assert.Equal("billing.invoice.paid.v1", row.SourceEventType);
            }
        );
    }

    [Fact]
    public async Task Two_paid_invoices_are_two_different_notices()
    {
        var h = new Harness(new Person(Owner, Office, IsOwner: true));

        await h.PaidAsync(Paid());
        await h.PaidAsync(Paid());

        Assert.Equal(2, h.InApp.Select(row => row.SourceEventId).Distinct().Count());
    }

    [Fact]
    public async Task Unapplied_payment_rings_the_owner_and_the_billing_subscribers_once_each()
    {
        var h = new Harness(WholeOffice());

        await h.RejectedAsync(Rejected());

        Assert.Equal(new[] { Owner, Subscriber }.Order(), h.InApp.Select(row => row.RecipientUserId).Order());
        Assert.All(
            h.InApp,
            row =>
            {
                Assert.Equal(NotificationKinds.BillingInvoicePaymentRejected, row.Kind);
                Assert.Equal(Office, row.TenantId);
                Assert.Equal("Billing", row.Category);
                Assert.Equal("invoice", row.ResourceType);
                Assert.Equal(InvoiceId.ToString(), row.ResourceId);
            }
        );
    }

    [Fact]
    public async Task Unapplied_payment_carries_the_amount_and_the_reason()
    {
        var h = new Harness(WholeOffice());

        await h.RejectedAsync(Rejected());

        var row = h.InApp[0];
        Assert.Equal("80.00 USD", row.Params["amount"]);
        Assert.Equal("InvoiceVoided", row.Params["reason"]);
        Assert.Equal("Payment of 80.00 USD not applied", row.FallbackTitle);
        Assert.Equal("A payment could not be matched to its invoice. Review it in Billing.", row.FallbackBody);
    }

    [Fact]
    public async Task Unapplied_payment_for_an_unknown_invoice_points_to_the_unapplied_payment()
    {
        var h = new Harness(WholeOffice());

        await h.RejectedAsync(Rejected() with { InvoiceId = null });

        Assert.All(h.InApp, row => Assert.Equal(UnappliedPaymentId.ToString(), row.ResourceId));
    }

    [Fact]
    public async Task Unapplied_payment_is_not_told_to_staff_without_invoices_or_without_the_subscription()
    {
        var h = new Harness(WholeOffice());

        await h.RejectedAsync(Rejected());

        var told = h.InApp.Select(row => row.RecipientUserId).ToArray();
        Assert.DoesNotContain(SubscriberWithoutInvoices, told);
        Assert.DoesNotContain(EmployeeNotSubscribed, told);
        Assert.DoesNotContain(SubscriberOfOtherOffice, told);
        h.AssertAskedOnlyForOwnerAndBillingSubscribersWhoSeeInvoices();
    }

    [Fact]
    public async Task Unapplied_payment_with_nobody_to_tell_publishes_nothing()
    {
        var h = new Harness(new Person(SubscriberOfOtherOffice, OtherOffice, IsOwner: true, BillingAlerts: true));

        await h.RejectedAsync(Rejected());

        Assert.Empty(h.InApp);
    }

    [Fact]
    public async Task A_redelivered_unapplied_payment_keeps_the_same_event_for_each_person()
    {
        var h = new Harness(WholeOffice());
        var evt = Rejected();

        await h.RejectedAsync(evt);
        await h.RejectedAsync(evt);

        Assert.Equal(4, h.InApp.Count);
        Assert.Equal(2, h.InApp.Select(row => (row.RecipientUserId, row.SourceEventId, row.Kind)).Distinct().Count());
        Assert.All(
            h.InApp,
            row =>
            {
                Assert.Equal(evt.EventId, row.SourceEventId);
                Assert.Equal("billing.invoice.payment_rejected.v1", row.SourceEventType);
            }
        );
    }

    [Theory]
    [InlineData(NotificationKinds.BillingInvoicePaid, NotificationPriority.Normal)]
    [InlineData(NotificationKinds.BillingInvoicePaymentRejected, NotificationPriority.High)]
    public void The_catalog_keeps_invoice_staff_notices_in_the_bell_only(string kind, NotificationPriority priority)
    {
        var entry = NotificationCatalog.GetRequired(kind);

        Assert.Equal(NotificationCatalogCategory.Billing, entry.Category);
        Assert.Equal(NotificationRecipientPolicy.ByPermission, entry.RecipientPolicy);
        Assert.Equal(priority, entry.DefaultPriority);
        Assert.Equal([NotificationChannel.InApp], entry.AllowedChannels);
    }

    private static async Task<string> PaymentSourceOfPaidAsync(string? paymentSource)
    {
        var h = new Harness(new Person(Owner, Office, IsOwner: true));
        await h.PaidAsync(Paid() with { PaymentSource = paymentSource });
        return Assert.Single(h.InApp).Params["paymentSource"];
    }

    /// <summary>Una oficina completa y un suscriptor de otra: solo el dueño y <see cref="Subscriber"/> deben enterarse.</summary>
    private static Person[] WholeOffice() =>
        [
            new(Owner, Office, IsOwner: true, BillingAlerts: true),
            new(Subscriber, Office, BillingAlerts: true),
            new(SubscriberWithoutInvoices, Office, BillingAlerts: true, CanViewInvoices: false),
            new(EmployeeNotSubscribed, Office),
            new(SubscriberOfOtherOffice, OtherOffice, IsOwner: true, BillingAlerts: true),
        ];

    private static InvoicePaidIntegrationEvent Paid() =>
        new()
        {
            TenantId = Office,
            CorrelationId = "corr-invoice-paid",
            OccurredOn = PaidAt,
            InvoiceId = InvoiceId,
            InvoiceNumber = "INV-2026-00007",
            CustomerId = Guid.Parse("90000000-0000-0000-0000-000000000007"),
            AmountPaidCents = 12_500,
            Currency = "usd",
            PaymentMethod = "Online",
            PaidAtUtc = PaidAt,
            CustomerName = "Jane Client",
            PaymentSource = "Online",
        };

    private static InvoicePaymentRejectedIntegrationEvent Rejected() =>
        new()
        {
            TenantId = Office,
            CorrelationId = "corr-payment-rejected",
            OccurredOn = PaidAt,
            UnappliedPaymentId = UnappliedPaymentId,
            TenantPaymentId = Guid.Parse("a0000000-0000-0000-0000-000000000007"),
            InvoiceId = InvoiceId,
            ExternalReferenceId = InvoiceId.ToString(),
            AmountCents = 8_000,
            Currency = "usd",
            Reason = "InvoiceVoided",
            PaidAtUtc = PaidAt,
        };

    private sealed record Person(
        Guid UserId,
        Guid TenantId,
        bool IsOwner = false,
        bool BillingAlerts = false,
        bool CanViewInvoices = true
    );

    private sealed class Harness(params Person[] people)
    {
        private readonly NotificationDispatcherHarness _dispatcher = new();
        private readonly OfficeDirectory _directory = new(people);
        private readonly TestCorrelationContext _correlation = new();

        public IReadOnlyList<InAppNotificationRequestedIntegrationEvent> InApp =>
            _dispatcher.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().ToArray();

        public Task PaidAsync(InvoicePaidIntegrationEvent evt) =>
            InvoicePaidStaffConsumer.Handle(
                evt,
                _dispatcher.Dispatcher,
                _directory,
                _correlation,
                CancellationToken.None
            );

        public Task RejectedAsync(InvoicePaymentRejectedIntegrationEvent evt) =>
            InvoicePaymentRejectedStaffConsumer.Handle(
                evt,
                _dispatcher.Dispatcher,
                _directory,
                _correlation,
                CancellationToken.None
            );

        public void AssertAskedOnlyForOwnerAndBillingSubscribersWhoSeeInvoices()
        {
            Assert.Equal(
                new NotificationAudience[]
                {
                    new TenantOwnerAudience(Office),
                    new ByPermission(Office, NotificationPermissions.BillingAlerts),
                },
                _directory.Requests.Select(request => request.Audience)
            );
            Assert.All(
                _directory.Requests,
                request => Assert.Equal(InvoicesView, request.Validation.RequiredCapability)
            );
        }
    }

    /// <summary>Directorio de la oficina: aplica la validación que pide el consumer, como el resolver real.</summary>
    private sealed class OfficeDirectory(IReadOnlyList<Person> people) : IRecipientResolver
    {
        public List<(NotificationAudience Audience, RecipientValidation Validation)> Requests { get; } = [];

        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            throw new NotSupportedException("Sin validación no se sabe si el destinatario puede ver facturas.");

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        )
        {
            Requests.Add((audience, validation));
            var matched = audience switch
            {
                TenantOwnerAudience owner => people.Where(p => p.TenantId == owner.TenantId && p.IsOwner),
                ByPermission { PermissionCode: NotificationPermissions.BillingAlerts } subscribers => people.Where(p =>
                    p.TenantId == subscribers.TenantId && p.BillingAlerts
                ),
                _ => [],
            };
            if (validation.RequiredCapability == InvoicesView)
                matched = matched.Where(p => p.CanViewInvoices);

            return Task.FromResult(Result.Success<IReadOnlyList<Guid>>(matched.Select(p => p.UserId).ToArray()));
        }
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
