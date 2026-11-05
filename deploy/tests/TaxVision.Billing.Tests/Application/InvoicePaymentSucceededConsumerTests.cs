using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Payments;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Application;

public sealed class InvoicePaymentSucceededConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SameTenantPaymentIdDeliveredTwice_AppliesOnce()
    {
        var invoice = IssuedInvoice(15_000);
        var bus = new FakeMessageBus();
        var payment = PaymentFor(invoice, Guid.NewGuid(), 5_000);

        await HandleAsync(payment, invoice, bus);
        await HandleAsync(payment, invoice, bus);

        Assert.Equal(5_000, invoice.AmountPaid.AmountCents);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Single(invoice.Payments);
    }

    [Fact]
    public async Task PartialOnlinePayment_ReEnsuresPayable()
    {
        // Cobro en línea por menos del saldo: el enlace tiene que pasar a cobrar el saldo nuevo.
        var invoice = IssuedInvoice(525_000);
        var bus = new FakeMessageBus();

        await HandleAsync(PaymentFor(invoice, Guid.NewGuid(), 100_000), invoice, bus);

        Assert.Equal(425_000, invoice.AmountDue.AmountCents);
        var ensure = Assert.Single(bus.Published.OfType<EnsureInvoicePaymentLinkCommand>());
        Assert.Equal(new EnsureInvoicePaymentLinkCommand(TenantId, invoice.Id), ensure);
    }

    [Fact]
    public async Task SamePartialPaymentDeliveredTwice_ReEnsuresOnce()
    {
        var invoice = IssuedInvoice(15_000);
        var bus = new FakeMessageBus();
        var payment = PaymentFor(invoice, Guid.NewGuid(), 5_000);

        await HandleAsync(payment, invoice, bus);
        await HandleAsync(payment, invoice, bus);

        Assert.Single(bus.Published.OfType<EnsureInvoicePaymentLinkCommand>());
    }

    [Fact]
    public async Task DistinctPaymentOnPaidInvoice_RecordsOverpaymentWithoutRepublishing()
    {
        var invoice = IssuedInvoice(10_000);
        var bus = new FakeMessageBus();
        var metrics = new RecordingMetrics();

        await HandleAsync(PaymentFor(invoice, Guid.NewGuid(), 10_000), invoice, bus, metrics);
        await HandleAsync(PaymentFor(invoice, Guid.NewGuid(), 10_000), invoice, bus, metrics);

        Assert.Equal(20_000, invoice.AmountPaid.AmountCents);
        Assert.Equal(1, metrics.Overpayments);
        Assert.Single(bus.Published.OfType<InvoicePaidIntegrationEvent>());
    }

    [Fact]
    public async Task TwoDistinctPartialPayments_Sum()
    {
        var invoice = IssuedInvoice(15_000);
        var bus = new FakeMessageBus();

        await HandleAsync(PaymentFor(invoice, Guid.NewGuid(), 5_000), invoice, bus);
        await HandleAsync(PaymentFor(invoice, Guid.NewGuid(), 10_000), invoice, bus);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(15_000, invoice.AmountPaid.AmountCents);
        Assert.Single(bus.Published.OfType<InvoicePaidIntegrationEvent>());
    }

    [Fact]
    public async Task PaymentOnVoidedInvoice_RecordsUnappliedAndPublishesRejected()
    {
        var invoice = IssuedInvoice(10_000);
        Assert.True(invoice.Void("Customer cancelled", Now, ActorUserId).IsSuccess);
        var bus = new FakeMessageBus();
        var unapplied = new InMemoryUnappliedPaymentRepository();
        var metrics = new RecordingMetrics();
        var payment = PaymentFor(invoice, Guid.NewGuid(), 10_000);

        await HandleAsync(payment, invoice, bus, metrics, unapplied);

        var row = Assert.Single(unapplied.Rows);
        Assert.Equal(UnappliedPaymentReason.InvoiceVoided, row.Reason);
        Assert.Equal(payment.TenantPaymentId, row.TenantPaymentId);
        Assert.Equal(invoice.Id, row.InvoiceId);
        var rejected = Assert.Single(bus.Published.OfType<InvoicePaymentRejectedIntegrationEvent>());
        Assert.Equal(nameof(UnappliedPaymentReason.InvoiceVoided), rejected.Reason);
        Assert.Equal(new[] { nameof(UnappliedPaymentReason.InvoiceVoided) }, metrics.Rejected);
    }

    [Fact]
    public async Task PaymentForUnknownInvoice_RecordsUnapplied()
    {
        var invoice = IssuedInvoice(10_000);
        var bus = new FakeMessageBus();
        var unapplied = new InMemoryUnappliedPaymentRepository();
        var payment = PaymentFor(invoice, Guid.NewGuid(), 10_000) with
        {
            ExternalReferenceId = Guid.NewGuid().ToString(),
        };

        await HandleAsync(payment, invoice, bus, unapplied: unapplied);

        var row = Assert.Single(unapplied.Rows);
        Assert.Equal(UnappliedPaymentReason.InvoiceNotFound, row.Reason);
        Assert.Single(bus.Published.OfType<InvoicePaymentRejectedIntegrationEvent>());
    }

    [Fact]
    public async Task CurrencyMismatch_RecordsUnapplied()
    {
        var invoice = IssuedInvoice(10_000);
        var bus = new FakeMessageBus();
        var unapplied = new InMemoryUnappliedPaymentRepository();
        var payment = PaymentFor(invoice, Guid.NewGuid(), 10_000) with { Currency = "EUR" };

        await HandleAsync(payment, invoice, bus, unapplied: unapplied);

        var row = Assert.Single(unapplied.Rows);
        Assert.Equal(UnappliedPaymentReason.CurrencyMismatch, row.Reason);
        Assert.Equal("EUR", row.Currency);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(0, invoice.AmountPaid.AmountCents);
        Assert.Empty(invoice.Payments);
    }

    [Fact]
    public async Task RejectedPaymentDeliveredTwice_RecordsOnce()
    {
        var invoice = IssuedInvoice(10_000);
        Assert.True(invoice.Void(null, Now, ActorUserId).IsSuccess);
        var bus = new FakeMessageBus();
        var unapplied = new InMemoryUnappliedPaymentRepository();
        var payment = PaymentFor(invoice, Guid.NewGuid(), 10_000);

        await HandleAsync(payment, invoice, bus, unapplied: unapplied);
        await HandleAsync(payment, invoice, bus, unapplied: unapplied);

        Assert.Single(unapplied.Rows);
        Assert.Single(bus.Published.OfType<InvoicePaymentRejectedIntegrationEvent>());
    }

    private static Task HandleAsync(
        TenantPaymentSucceededIntegrationEvent payment,
        Invoice invoice,
        FakeMessageBus bus,
        RecordingMetrics? metrics = null,
        InMemoryUnappliedPaymentRepository? unapplied = null
    ) =>
        InvoicePaymentSucceededConsumer.Handle(
            payment,
            new SingleInvoiceRepository(invoice),
            unapplied ?? new InMemoryUnappliedPaymentRepository(),
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            metrics ?? new RecordingMetrics(),
            NullLogger<Invoice>.Instance,
            CancellationToken.None
        );

    private sealed class RecordingMetrics : IBillingMetrics
    {
        public int Overpayments { get; private set; }
        public List<string> Rejected { get; } = [];

        public void RecordOverpayment() => Overpayments++;

        public void RecordPaymentRejected(string reason) => Rejected.Add(reason);

        public void RecordStockSyncInsufficient() { }
    }

    private static TenantPaymentSucceededIntegrationEvent PaymentFor(Invoice invoice, Guid paymentId, long cents) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "online-payment",
            TenantPaymentId = paymentId,
            ProviderCode = "Stripe",
            PurposeKind = "InvoicePayment",
            ExternalReferenceId = invoice.Id.ToString(),
            AmountCents = cents,
            Currency = invoice.Currency,
            PaidAtUtc = Now,
        };

    private static Invoice IssuedInvoice(long totalCents)
    {
        var invoice = Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, totalCents, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue("INV-2026-00001", Now, Now.AddDays(30), ActorUserId).IsSuccess);
        return invoice;
    }

    private sealed class SingleInvoiceRepository(Invoice invoice) : IInvoiceRepository
    {
        public Task<Invoice?> GetByIdAsync(
            Guid tenantId,
            Guid invoiceId,
            CancellationToken ct = default,
            Guid? assignedToUserId = null
        ) => Task.FromResult<Invoice?>(invoice.Id == invoiceId ? invoice : null);

        public Task<Invoice?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            Task.FromResult<Invoice?>(null);

        public Task<IReadOnlyList<Invoice>> ListByTenantAsync(
            Guid tenantId,
            int take,
            CancellationToken ct = default,
            Guid? assignedToUserId = null,
            Guid? customerId = null
        ) => Task.FromResult<IReadOnlyList<Invoice>>([invoice]);

        public Task AddAsync(Invoice value, CancellationToken ct = default) => Task.CompletedTask;
    }
}
