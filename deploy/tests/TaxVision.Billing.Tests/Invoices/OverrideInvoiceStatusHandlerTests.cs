using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.OverrideInvoiceStatus;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>
/// La corrección de estado reconcilia lo que cuelga del estado por sus vías de siempre: el cobro en PaymentClient
/// (revocar o asegurar), el PDF y el stock. Se comprueba qué sale según el estado al que se lleva la factura.
/// </summary>
public sealed class OverrideInvoiceStatusHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Reopening_a_paid_invoice_as_a_draft_revokes_the_payable_and_reconciles_stock()
    {
        var bus = new FakeMessageBus();
        var invoice = PaidInvoice(InvoicePaymentSource.Online);

        var result = await RunAsync(invoice, "Draft", bus);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Changed);
        Assert.Equal("Draft", result.Value.Status);
        // El cobro online revertido se avisa: hay que reembolsarlo en el proveedor.
        var reversed = Assert.Single(result.Value.ReversedPayments);
        Assert.True(reversed.NeedsProviderRefund);
        var revoked = Assert.Single(bus.Published.OfType<InvoiceVoidedIntegrationEvent>());
        Assert.Equal(OverrideInvoiceStatusHandler.ReopenedAsDraftReason, revoked.Reason);
        Assert.Equal(TenantId, revoked.TenantId);
        Assert.Single(bus.Published.OfType<SyncInvoiceStockCommand>());
        Assert.Empty(bus.Published.OfType<EnsureInvoicePaymentLinkCommand>());
    }

    [Fact]
    public async Task Reopening_a_paid_invoice_as_issued_makes_it_payable_again()
    {
        var bus = new FakeMessageBus();
        var invoice = PaidInvoice(InvoicePaymentSource.Manual);

        var result = await RunAsync(invoice, "Issued", bus);

        Assert.True(result.IsSuccess);
        Assert.Equal("Issued", result.Value.Status);
        // Reinstate: la oficina la dejó cobrable a propósito, así que un cobro revocado se reinstala.
        Assert.True(Assert.Single(bus.Published.OfType<EnsureInvoicePaymentLinkCommand>()).Reinstate);
        Assert.Empty(bus.Published.OfType<InvoiceVoidedIntegrationEvent>());
        Assert.Single(bus.Published.OfType<SyncInvoiceStockCommand>());
    }

    [Fact]
    public async Task Voiding_revokes_the_payable_and_regenerates_the_pdf()
    {
        var bus = new FakeMessageBus();

        var result = await RunAsync(IssuedInvoice(), "Voided", bus);

        Assert.True(result.IsSuccess);
        var voided = Assert.Single(bus.Published.OfType<InvoiceVoidedIntegrationEvent>());
        Assert.Equal("Corrección pedida por la oficina", voided.Reason);
        Assert.Single(bus.Published.OfType<GenerateInvoicePdfCommand>());
        Assert.Single(bus.Published.OfType<SyncInvoiceStockCommand>());
    }

    [Fact]
    public async Task Marking_paid_records_a_manual_payment_and_ensures_the_link()
    {
        var bus = new FakeMessageBus();

        var result = await RunAsync(IssuedInvoice(), "Paid", bus);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.PaymentRecorded);
        Assert.Empty(result.Value.ReversedPayments);
        Assert.Single(bus.Published.OfType<EnsureInvoicePaymentLinkCommand>());
    }

    [Fact]
    public async Task The_same_status_changes_nothing_and_publishes_nothing()
    {
        var bus = new FakeMessageBus();
        var unitOfWork = new CountingUnitOfWork();

        var result = await RunAsync(IssuedInvoice(), "Issued", bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Changed);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task A_failed_save_publishes_nothing()
    {
        var bus = new FakeMessageBus();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            RunAsync(PaidInvoice(InvoicePaymentSource.Manual), "Draft", bus, new FailingUnitOfWork())
        );

        Assert.Empty(bus.Published);
    }

    [Theory]
    [InlineData("Nonsense", "Billing.Invoice.UnknownStatus")]
    [InlineData("PartiallyPaid", "Billing.Invoice.StatusNotSettable")]
    public async Task Invalid_targets_are_rejected_without_publishing(string target, string code)
    {
        var bus = new FakeMessageBus();

        var result = await RunAsync(IssuedInvoice(), target, bus);

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task A_missing_reason_is_rejected()
    {
        var bus = new FakeMessageBus();

        var result = await RunAsync(IssuedInvoice(), "Sent", bus, reason: " ");

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.ReasonRequired", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    private static Task<BuildingBlocks.Results.Result<OverrideInvoiceStatusResult>> RunAsync(
        Invoice invoice,
        string target,
        FakeMessageBus bus,
        IUnitOfWork? unitOfWork = null,
        string reason = "Corrección pedida por la oficina"
    ) =>
        OverrideInvoiceStatusHandler.Handle(
            new OverrideInvoiceStatusCommand(TenantId, invoice.Id, target, reason, ActorUserId, CanViewAll: true),
            new SingleInvoice(invoice),
            Options.Create(new BillingVisibilityOptions()),
            unitOfWork ?? new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            new FixedClock(Now.AddHours(1)),
            CancellationToken.None
        );

    private static Invoice PaidInvoice(InvoicePaymentSource source)
    {
        var invoice = IssuedInvoice();
        Assert.True(
            invoice
                .ApplyPayment(
                    source,
                    "pay-1",
                    Money.Create(10_000, "USD").Value,
                    source == InvoicePaymentSource.Online ? PaymentMethod.Online : PaymentMethod.Cash,
                    Now,
                    ActorUserId
                )
                .IsSuccess
        );
        return invoice;
    }

    private static Invoice IssuedInvoice()
    {
        var invoice = Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 10_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(
            invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess
        );
        return invoice;
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Simulated SQL timeout.");
    }

    private sealed class SingleInvoice(Invoice invoice) : IInvoiceRepository
    {
        public Task<Invoice?> GetByIdAsync(
            Guid tenantId,
            Guid invoiceId,
            CancellationToken ct = default,
            Guid? assignedToUserId = null
        ) => Task.FromResult<Invoice?>(invoice);

        public Task<IReadOnlyList<Invoice>> ListByTenantAsync(
            Guid tenantId,
            int take,
            CancellationToken ct = default,
            Guid? assignedToUserId = null,
            Guid? customerId = null
        ) => throw new NotSupportedException();

        public Task AddAsync(Invoice invoice, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
