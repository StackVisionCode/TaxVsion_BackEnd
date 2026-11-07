using BuildingBlocks.Messaging.BillingIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.RecordManualPayment;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Application;

public sealed class RecordManualPaymentHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NoAmount_DefaultsToAmountDue()
    {
        var invoice = Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 15_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess);

        Assert.True((await RecordAsync(invoice, 5_000)).IsSuccess);
        Assert.True((await RecordAsync(invoice, 5_000)).IsSuccess);
        Assert.Equal(10_000, invoice.AmountPaid.AmountCents);
        Assert.True((await RecordAsync(invoice, null)).IsSuccess);

        Assert.Equal(15_000, invoice.AmountPaid.AmountCents);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(3, invoice.Payments.Count);
        Assert.Equal(5_000, invoice.Payments.Last().Amount.AmountCents);
    }

    [Fact]
    public async Task NoAmountOnInvoiceWithoutBalance_ReturnsNoBalanceDue()
    {
        var invoice = Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 15_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess);
        Assert.True((await RecordAsync(invoice, null)).IsSuccess);

        var result = await RecordAsync(invoice, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.NoBalanceDue", result.Error.Code);
        Assert.Equal("This invoice has no balance due.", result.Error.Message);
        Assert.Single(invoice.Payments);
    }

    [Fact]
    public async Task SameClientRequestIdTwice_RecordsOnePayment()
    {
        var invoice = Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 15_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess);

        Assert.True((await RecordAsync(invoice, 5_000, "click-1")).IsSuccess);
        Assert.True((await RecordAsync(invoice, 5_000, "click-1")).IsSuccess);

        Assert.Single(invoice.Payments);
        Assert.Equal(5_000, invoice.AmountPaid.AmountCents);
    }

    [Fact]
    public async Task PartialPayment_ReEnsuresPayableWithAmountDue()
    {
        // T-VIVO INV-2026-00004: $5,250 con pago manual de $1,000; el enlace seguía cobrando $5,250.
        var invoice = IssuedInvoice(525_000);
        var bus = new FakeMessageBus();

        Assert.True((await RecordAsync(invoice, 100_000, bus: bus)).IsSuccess);

        Assert.Equal(425_000, invoice.AmountDue.AmountCents);
        var ensure = Assert.Single(bus.Published.OfType<EnsureInvoicePaymentLinkCommand>());
        Assert.Equal(new EnsureInvoicePaymentLinkCommand(TenantId, invoice.Id), ensure);
        Assert.Equal(TenantId.ToString(), bus.TenantId);
    }

    [Fact]
    public async Task FullPayment_DoesNotReEnsurePayable()
    {
        // Pagada: no hay saldo que cobrar; sale InvoicePaid y PaymentClient liquida el payable.
        var invoice = IssuedInvoice(15_000);
        var bus = new FakeMessageBus();

        Assert.True((await RecordAsync(invoice, null, bus: bus)).IsSuccess);

        Assert.Empty(bus.Published.OfType<EnsureInvoicePaymentLinkCommand>());
        Assert.Single(DomainEventReplay.Dispatch([invoice]).OfType<InvoicePaidIntegrationEvent>());
        Assert.Empty(bus.Published.OfType<InvoicePaidIntegrationEvent>());
    }

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
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00004").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess);
        return invoice;
    }

    private static Task<BuildingBlocks.Results.Result<RecordManualPaymentResult>> RecordAsync(
        Invoice invoice,
        long? amountCents,
        string? clientRequestId = null,
        FakeMessageBus? bus = null
    ) =>
        RecordManualPaymentHandler.Handle(
            new RecordManualPaymentCommand(
                TenantId,
                invoice.Id,
                PaymentMethod.Cash.ToString(),
                amountCents,
                Now,
                ActorUserId,
                CanViewAll: true,
                clientRequestId
            ),
            new SingleInvoiceRepository(invoice),
            Options.Create(new BillingVisibilityOptions()),
            new CountingUnitOfWork(),
            bus ?? new FakeMessageBus(),
            new FixedClock(Now),
            CancellationToken.None
        );

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
}
