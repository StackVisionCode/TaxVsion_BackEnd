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

    private static async Task<Invoice> IssueAsync(InvoiceTermsSnapshot? terms)
    {
        var draft = Invoice
            .CreateDraft(
                Tenant,
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", null, null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, 10_000, 0)],
                null,
                Now,
                null,
                terms
            )
            .Value;

        var result = await IssueInvoiceHandler.Handle(
            new IssueInvoiceCommand(Tenant, draft.Id, Guid.NewGuid(), CanViewAll: true),
            new SingleInvoice(draft),
            Options.Create(new BillingVisibilityOptions()),
            new NewSequence(),
            new NoStock(),
            new CountingUnitOfWork(),
            new FakeMessageBus(),
            new NoopCorrelationContext(),
            new FixedClock(Now),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return draft;
    }

    private sealed class SingleInvoice(Invoice invoice) : IInvoiceRepository
    {
        public Task<Invoice?> GetByIdAsync(
            Guid tenantId,
            Guid invoiceId,
            CancellationToken ct = default,
            Guid? assignedToUserId = null
        ) => Task.FromResult<Invoice?>(invoice);

        public Task<Invoice?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

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
        public Task<InvoiceNumberSequence> GetOrCreateAsync(
            Guid tenantId,
            string periodKey,
            CancellationToken ct = default
        ) => Task.FromResult(InvoiceNumberSequence.Start(tenantId, periodKey));
    }

    private sealed class NoStock : IInventoryStockClient
    {
        public Task<Result> CommitInvoiceSaleAsync(
            Guid tenantId,
            Guid invoiceId,
            IReadOnlyList<InvoiceSaleLine> lines,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success());
    }
}
