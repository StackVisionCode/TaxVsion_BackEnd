using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.IssueInvoice;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Numbering;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>
/// Aceptación de la Fase 6 (P6), de punta a punta: emitir y generar el PDF. Una factura emitida a las 20:00 de Nueva
/// York el 31-dic sale con fecha 31-dic y número del año en curso; una factura vieja re-renderizada conserva su fecha.
/// </summary>
public sealed class OfficeDatesAcceptanceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime NewYearsEveEveningInNewYork = new(2027, 1, 1, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_invoice_issued_on_new_years_eve_in_new_york_is_dated_and_numbered_in_that_year()
    {
        var draft = Draft();
        var invoices = new InMemoryInvoiceRepository(draft);

        var issued = await IssueInvoiceHandler.Handle(
            new IssueInvoiceCommand(Tenant, draft.Id, Guid.NewGuid(), CanViewAll: true),
            invoices,
            Options.Create(new BillingVisibilityOptions()),
            new FirstNumber(),
            new RecordingInventoryClient(),
            new CountingUnitOfWork(),
            new FakeMessageBus(),
            new FixedClock(NewYearsEveEveningInNewYork),
            new OfficeIn("America/New_York"),
            CancellationToken.None
        );
        var pdf = await RenderAsync(draft);

        Assert.Equal("INV-2026-00001", issued.Value.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 12, 31), pdf.IssueDate);
        Assert.Equal(2026, pdf.TaxYear);
        Assert.Equal(new DateOnly(2027, 1, 30), pdf.DueDate);
    }

    [Fact]
    public async Task An_old_invoice_rendered_again_keeps_the_date_it_already_printed()
    {
        var old = Draft();
        old.Issue(
            InvoiceNumber.Create("INV-2027-00001").Value,
            NewYearsEveEveningInNewYork,
            NewYearsEveEveningInNewYork.AddDays(30),
            Guid.NewGuid()
        );

        var pdf = await RenderAsync(old);

        Assert.Equal(new DateOnly(2027, 1, 1), pdf.IssueDate);
        Assert.Equal(2027, pdf.TaxYear);
    }

    private static async Task<InvoiceDocumentRequest> RenderAsync(Invoice invoice)
    {
        var documents = new RecordingDocuments();
        await GenerateInvoicePdfHandler.Handle(
            new GenerateInvoicePdfCommand(Tenant, invoice.Id),
            new InMemoryInvoiceRepository(invoice),
            documents,
            new NoopCorrelationContext(),
            CancellationToken.None
        );
        return Assert.IsType<InvoiceDocumentRequest>(documents.Sent);
    }

    private static Invoice Draft() =>
        Invoice
            .CreateDraft(
                Tenant,
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, 10_000, 0)],
                null,
                NewYearsEveEveningInNewYork,
                null,
                new InvoiceTermsSnapshot(PaymentTerms.Net30, null, null)
            )
            .Value;

    private sealed class OfficeIn(string zone) : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(ResolvedTimeZone.Resolve(zone));
    }

    private sealed class FirstNumber : IInvoiceNumberSequenceRepository
    {
        public Task<InvoiceNumberSequence> GetOrCreateAsync(
            Guid tenantId,
            string periodKey,
            CancellationToken ct = default
        ) => Task.FromResult(InvoiceNumberSequence.Start(tenantId, periodKey));
    }

    private sealed class RecordingDocuments : IInvoiceDocumentClient
    {
        public InvoiceDocumentRequest? Sent { get; private set; }

        public Task<Result> GenerateAsync(
            InvoiceDocumentRequest request,
            Guid tenantId,
            string idempotencyKey,
            string? correlationId,
            CancellationToken ct = default
        )
        {
            Sent = request;
            return Task.FromResult(Result.Success());
        }
    }
}
