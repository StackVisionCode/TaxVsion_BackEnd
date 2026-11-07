using BuildingBlocks.Common;
using BuildingBlocks.Domain;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;
using TaxVision.Billing.Application.Invoices.IssueInvoice;
using TaxVision.Billing.Application.Invoices.RecordManualPayment;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Numbering;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>
/// Un solo InvoicePaid por pago: cuenta lo publicado a mano más lo que devuelve el handler del evento de dominio.
/// Si conviven los dos caminos, el aviso sale dos veces y esto falla. Ningún evento de dominio sale por el bus.
/// </summary>
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
            TimeProvider.System,
            CancellationToken.None
        );
        await RecordManualPaymentHandler.Handle(
            command,
            new InvoiceRepositoryStub(invoice),
            Options.Create(new BillingVisibilityOptions()),
            new UnitOfWorkStub(),
            bus,
            TimeProvider.System,
            CancellationToken.None
        );

        var emitted = Emitted(bus, invoice);
        var published = Assert.Single(emitted.OfType<InvoicePaidIntegrationEvent>());
        Assert.Equal(invoice.Id, published.InvoiceId);
        Assert.Equal(DomainEventReplay.CorrelationId, published.CorrelationId);
        Assert.Null(published.ReceiptId);
        Assert.Single(emitted.OfType<GenerateInvoicePdfCommand>());
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
            new InMemoryUnappliedPaymentRepository(),
            new UnitOfWorkStub(),
            bus,
            correlation,
            new NoopMetrics(),
            NullLogger<Invoice>.Instance,
            CancellationToken.None
        );
        await InvoicePaymentSucceededConsumer.Handle(
            payment,
            repository,
            new InMemoryUnappliedPaymentRepository(),
            new UnitOfWorkStub(),
            bus,
            correlation,
            new NoopMetrics(),
            NullLogger<Invoice>.Instance,
            CancellationToken.None
        );

        var emitted = Emitted(bus, invoice);
        var published = Assert.Single(emitted.OfType<InvoicePaidIntegrationEvent>());
        Assert.Equal(PaymentMethod.Online.ToString(), published.PaymentMethod);
        Assert.Equal(invoice.AmountPaid.AmountCents, published.AmountPaidCents);
        Assert.Single(emitted.OfType<GenerateInvoicePdfCommand>());
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
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var emitted = Emitted(bus, invoice);
        var published = Assert.Single(emitted.OfType<InvoicePaidIntegrationEvent>());
        Assert.Equal(invoice.AmountPaid.AmountCents, published.AmountPaidCents);
        Assert.Equal(invoice.InvoiceNumber, published.InvoiceNumber);
        // El PDF de la emisión lo encadena EnsureInvoicePaymentLink; no se pide otro.
        Assert.Single(emitted.OfType<EnsureInvoicePaymentLinkCommand>());
        Assert.Empty(emitted.OfType<GenerateInvoicePdfCommand>());
    }

    // Lo publicado a mano más lo que el commit despacharía por la cola domain-events.
    private static IReadOnlyList<object> Emitted(FakeMessageBus bus, Invoice invoice)
    {
        Assert.DoesNotContain(bus.Published, m => m is IDomainEvent);
        return [.. bus.Published, .. DomainEventReplay.Dispatch([invoice])];
    }

    private static Invoice CreateIssuedInvoice()
    {
        var invoice = CreateDraftInvoice();
        var issued = invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), ActorUserId);
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

        public Task<Invoice?> GetOnboardingInvoiceByIdAsync(Guid invoiceId, CancellationToken ct = default) =>
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

        public Task<Result> CheckInvoiceSaleAsync(
            Guid tenantId,
            Guid invoiceId,
            IReadOnlyList<InvoiceSaleLine> lines,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success());
    }

    private sealed class NoopMetrics : IBillingMetrics
    {
        public void RecordOverpayment() { }

        public void RecordPaymentRejected(string reason) { }

        public void RecordStockSyncInsufficient() { }
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
