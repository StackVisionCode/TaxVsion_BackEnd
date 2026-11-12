using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.IssueInvoice;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Numbering;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>Al emitir, el vencimiento sale del plazo congelado en el borrador.</summary>
public sealed class IssueInvoiceHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(PaymentTerms.DueOnReceipt, 0)]
    [InlineData(PaymentTerms.Net15, 15)]
    public async Task The_due_date_comes_from_the_frozen_terms(PaymentTerms terms, int days)
    {
        var invoice = await IssueAsync(new InvoiceTermsSnapshot(terms, null, null));

        Assert.Equal(Now.AddDays(days), invoice.DueDateUtc);
    }

    [Fact]
    public async Task A_draft_from_before_the_settings_keeps_30_days()
    {
        var invoice = await IssueAsync(null);

        Assert.Equal(Now.AddDays(30), invoice.DueDateUtc);
    }

    // BIL-16: Wolverine guarda aunque se devuelva Failure, así que el número reservado se perdería.
    [Fact]
    public async Task An_invoice_that_is_not_a_draft_does_not_take_a_number()
    {
        var invoice = await IssueAsync(null);
        var sequences = new NewSequence();

        var result = await HandleAsync(invoice, sequences);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.NotDraft", result.Error.Code);
        Assert.Equal(0, sequences.Requests);
    }

    [Fact]
    public async Task An_invoice_that_is_not_a_draft_does_not_ask_for_stock()
    {
        var invoice = await IssueAsync(null, new DraftInvoiceLine("Ink", 1, 2_000, 0, Guid.NewGuid()));
        var stock = new NoStock();

        await HandleAsync(invoice, new NewSequence(), stock);

        Assert.Equal(0, stock.Checks);
    }

    [Fact]
    public async Task A_draft_keeps_the_number_format_and_advances_the_sequence()
    {
        var draft = Draft(null);
        var sequences = new NewSequence();

        var result = await HandleAsync(draft, sequences);

        Assert.True(result.IsSuccess);
        Assert.Equal($"INV-{Now.Year}-00001", result.Value.InvoiceNumber);
        Assert.Equal(2, sequences.Last!.Next);
    }

    private static Invoice Draft(InvoiceTermsSnapshot? terms, DraftInvoiceLine? line = null) =>
        Invoice
            .CreateDraft(
                Tenant,
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", null, null, null, null),
                "USD",
                [line ?? new DraftInvoiceLine("Tax prep", 1, 10_000, 0)],
                null,
                Now,
                null,
                terms
            )
            .Value;

    private static async Task<Invoice> IssueAsync(InvoiceTermsSnapshot? terms, DraftInvoiceLine? line = null)
    {
        var draft = Draft(terms, line);
        var result = await HandleAsync(draft, new NewSequence());
        Assert.True(result.IsSuccess);
        return draft;
    }

    private static Task<Result<IssueInvoiceResult>> HandleAsync(
        Invoice invoice,
        NewSequence sequences,
        NoStock? stock = null
    ) =>
        IssueInvoiceHandler.Handle(
            new IssueInvoiceCommand(Tenant, invoice.Id, Guid.NewGuid(), CanViewAll: true),
            new SingleInvoice(invoice),
            Options.Create(new BillingVisibilityOptions()),
            sequences,
            stock ?? new NoStock(),
            new CountingUnitOfWork(),
            new FakeMessageBus(),
            new FixedClock(Now),
            CancellationToken.None
        );

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

        public Task AddAsync(Invoice value, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NewSequence : IInvoiceNumberSequenceRepository
    {
        public int Requests { get; private set; }
        public InvoiceNumberSequence? Last { get; private set; }

        public Task<InvoiceNumberSequence> GetOrCreateAsync(
            Guid tenantId,
            string periodKey,
            CancellationToken ct = default
        )
        {
            Requests++;
            Last = InvoiceNumberSequence.Start(tenantId, periodKey);
            return Task.FromResult(Last);
        }
    }

    private sealed class NoStock : IInventoryStockClient
    {
        public int Checks { get; private set; }

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
        )
        {
            Checks++;
            return Task.FromResult(Result.Success());
        }
    }
}
