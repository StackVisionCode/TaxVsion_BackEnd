using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;
using TaxVision.Billing.Application.Invoices.IssueInvoice;
using TaxVision.Billing.Application.Invoices.RecordManualPayment;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Numbering;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

public sealed class InvoicePaidPublishingTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Manual_payment_publishes_once_when_invoice_becomes_paid()
    {
        var invoice = CreateIssuedInvoice();
        var bus = new FakeMessageBus();
        var command = new RecordManualPaymentCommand(
            TenantId,
            invoice.Id,
            PaymentMethod.Cash.ToString(),
            invoice.Total.AmountCents,
            Now.AddHours(1),
            ActorUserId,
            CanViewAll: true
        );

        await RecordManualPaymentHandler.Handle(
            command,
            new InvoiceRepositoryStub(invoice),
            Options.Create(new BillingVisibilityOptions()),
            new UnitOfWorkStub(),
            bus,
            new CorrelationContextStub("manual-payment"),
            TimeProvider.System,
            CancellationToken.None
        );
        await RecordManualPaymentHandler.Handle(
            command,
            new InvoiceRepositoryStub(invoice),
            Options.Create(new BillingVisibilityOptions()),
            new UnitOfWorkStub(),
            bus,
            new CorrelationContextStub("manual-payment"),
            TimeProvider.System,
            CancellationToken.None
        );

        var published = Assert.Single(bus.Published.OfType<InvoicePaidIntegrationEvent>());
        Assert.Equal(invoice.Id, published.InvoiceId);
        Assert.Equal("manual-payment", published.CorrelationId);
        Assert.Null(published.ReceiptId);
    }

    [Fact]
    public async Task Online_payment_publishes_once_when_invoice_becomes_paid()
    {
        var invoice = CreateIssuedInvoice();
        var bus = new FakeMessageBus();
        var payment = new TenantPaymentSucceededIntegrationEvent
        {
            TenantId = TenantId,
            CorrelationId = "online-payment",
            TenantPaymentId = Guid.NewGuid(),
            ProviderCode = "Stripe",
            PurposeKind = "InvoicePayment",
            ExternalReferenceId = invoice.Id.ToString(),
            AmountCents = invoice.Total.AmountCents,
            Currency = invoice.Currency,
            PaidAtUtc = Now.AddHours(1),
        };
        var repository = new InvoiceRepositoryStub(invoice);
        var correlation = new CorrelationContextStub();

        await InvoicePaymentSucceededConsumer.Handle(
            payment,
            repository,
            new UnitOfWorkStub(),
            bus,
            correlation,
            NullLogger<Invoice>.Instance,
            CancellationToken.None
        );
        await InvoicePaymentSucceededConsumer.Handle(
            payment,
            repository,
            new UnitOfWorkStub(),
            bus,
            correlation,
            NullLogger<Invoice>.Instance,
            CancellationToken.None
        );

        var published = Assert.Single(bus.Published.OfType<InvoicePaidIntegrationEvent>());
        Assert.Equal("online-payment", published.CorrelationId);
        Assert.Equal(PaymentMethod.Online.ToString(), published.PaymentMethod);
    }

    [Fact]
    public async Task Carried_credit_publishes_when_replacement_is_fully_paid()
    {
        var invoice = CreateDraftInvoice();
        var linked = invoice.LinkAsReplacementFor(Guid.NewGuid(), invoice.Total.AmountCents, Now);
        Assert.True(linked.IsSuccess);
        var bus = new FakeMessageBus();

        var result = await IssueInvoiceHandler.Handle(
            new IssueInvoiceCommand(TenantId, invoice.Id, ActorUserId, CanViewAll: true),
            new InvoiceRepositoryStub(invoice),
            Options.Create(new BillingVisibilityOptions()),
            new SequenceRepositoryStub(),
            new InventoryClientStub(),
            new UnitOfWorkStub(),
            bus,
            new CorrelationContextStub("carried-credit"),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var published = Assert.Single(bus.Published.OfType<InvoicePaidIntegrationEvent>());
        Assert.Equal(invoice.AmountPaid.AmountCents, published.AmountPaidCents);
        Assert.Equal("carried-credit", published.CorrelationId);
    }

    private static Invoice CreateIssuedInvoice()
    {
        var invoice = CreateDraftInvoice();
        var issued = invoice.Issue("INV-2026-00001", Now, Now.AddDays(30), ActorUserId);
        Assert.True(issued.IsSuccess);
        return invoice;
    }

    private static Invoice CreateDraftInvoice()
    {
        var result = Invoice.CreateDraft(
            TenantId,
            ActorUserId,
            new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
            "USD",
            [new DraftInvoiceLine("Tax preparation", 1, 10_000, 0)],
            null,
            Now
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class InvoiceRepositoryStub(Invoice invoice) : IInvoiceRepository
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

    private sealed class SequenceRepositoryStub : IInvoiceNumberSequenceRepository
    {
        public Task<InvoiceNumberSequence> GetOrCreateAsync(
            Guid tenantId,
            string periodKey,
            CancellationToken ct = default
        ) => Task.FromResult(InvoiceNumberSequence.Start(tenantId, periodKey));
    }

    private sealed class InventoryClientStub : IInventoryStockClient
    {
        public Task<Result> CommitInvoiceSaleAsync(
            Guid tenantId,
            Guid invoiceId,
            IReadOnlyList<InvoiceSaleLine> lines,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success());
    }

    private sealed class UnitOfWorkStub : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class CorrelationContextStub(string correlationId = "") : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = correlationId;

        public void Set(string value) => CorrelationId = value;

        public IDisposable Push(string value)
        {
            var previous = CorrelationId;
            CorrelationId = value;
            return new CallbackDisposable(() => CorrelationId = previous);
        }
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }
}
