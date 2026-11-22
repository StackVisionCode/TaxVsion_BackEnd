using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>El PDF recibe lo mismo que muestra la vista previa del CRM: además de nombre, EIN y
/// dirección, el teléfono, correo y web del emisor congelados en el snapshot.</summary>
public sealed class GenerateInvoicePdfHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task The_issuer_contact_details_reach_documents()
    {
        var issuer = new IssuerSnapshot(
            "Gutierrez Tax Office",
            new Address("1600 Main St", "Suite 200", "Austin", "TX", "78701", "US"),
            "(512) 555-0112",
            "office@gutierreztax.com",
            "https://gutierreztax.com",
            null,
            "12-3456789"
        );

        var sent = await GenerateAsync(issuer);

        Assert.Equal("Gutierrez Tax Office", sent.Issuer.Name);
        Assert.Equal("12-3456789", sent.Issuer.TaxId);
        Assert.Equal("1600 Main St, Suite 200, Austin, TX 78701, US", sent.Issuer.Address);
        Assert.Equal("(512) 555-0112", sent.Issuer.Phone);
        Assert.Equal("office@gutierreztax.com", sent.Issuer.Email);
        Assert.Equal("https://gutierreztax.com", sent.Issuer.Website);
    }

    [Fact]
    public async Task An_old_snapshot_without_contact_or_address_still_renders()
    {
        // Snapshot de antes de F0: sin contacto y con el guion largo como dirección de relleno.
        var issuer = new IssuerSnapshot("Old office", new Address("—", null, "", "", "", "US"), null, null, null, null);

        var sent = await GenerateAsync(issuer);

        Assert.Equal("Old office", sent.Issuer.Name);
        Assert.Null(sent.Issuer.Address);
        Assert.Null(sent.Issuer.Phone);
        Assert.Null(sent.Issuer.Email);
        Assert.Null(sent.Issuer.Website);
    }

    [Fact]
    public async Task The_customer_does_not_carry_contact_details_to_the_pdf()
    {
        var sent = await GenerateAsync(null);

        Assert.Null(sent.Customer.Phone);
        Assert.Null(sent.Customer.Email);
    }

    [Fact]
    public async Task The_frozen_terms_reach_documents_with_a_readable_label()
    {
        var sent = await GenerateAsync(
            null,
            new InvoiceTermsSnapshot(PaymentTerms.Net30, "Zelle to pay@office.com", "Thank you!")
        );

        Assert.Equal("Net 30", sent.PaymentTerms);
        Assert.Equal("Zelle to pay@office.com", sent.PaymentInstructions);
        Assert.Equal("Thank you!", sent.InvoiceFooter);
    }

    [Fact]
    public async Task An_invoice_from_before_the_settings_sends_no_terms()
    {
        var sent = await GenerateAsync(null);

        Assert.Null(sent.PaymentTerms);
        Assert.Null(sent.PaymentInstructions);
        Assert.Null(sent.InvoiceFooter);
    }

    // BIL-PDF1: el pago parcial cambia el saldo y con él la clave; sin pago nuevo, la clave no cambia.
    [Fact]
    public async Task PartialPayment_SendsBalanceAndNewIdempotencyKey()
    {
        var invoice = Issued(525000);
        var documents = new RecordingDocuments();
        await RunAsync(invoice, documents);
        var firstKey = documents.Key;

        Pay(invoice, 100000);
        await RunAsync(invoice, documents);

        Assert.NotEqual(firstKey, documents.Key);
        Assert.Equal(1000.00m, documents.Sent!.AmountPaid);
        Assert.Equal(4250.00m, documents.Sent.AmountDue);
    }

    [Fact]
    public async Task SameBalance_ReusesIdempotencyKey()
    {
        var invoice = Issued(525000);
        Pay(invoice, 100000);
        var documents = new RecordingDocuments();

        await RunAsync(invoice, documents);
        var firstKey = documents.Key;
        await RunAsync(invoice, documents);

        Assert.Equal(firstKey, documents.Key);
    }

    // BIL-7: con la misma clave, Documents devuelve el PDF "pendiente" y la anulada nunca sale con su marca.
    [Fact]
    public async Task VoidedInvoice_UsesDistinctIdempotencyKey()
    {
        var invoice = Issued(80000);
        invoice.AttachPaymentLink(Guid.NewGuid(), "https://pay.example.test/x", Now);
        var documents = new RecordingDocuments();
        await RunAsync(invoice, documents);
        var issuedKey = documents.Key;

        Assert.True(invoice.Void("mistake", Now, Actor).IsSuccess);
        await RunAsync(invoice, documents);

        Assert.NotEqual(issuedKey, documents.Key);
    }

    // BIL-7: la anulada ya sale con su propio PDF; ese PDF no ofrece pagar.
    [Fact]
    public async Task VoidedInvoice_PdfOffersNoPayment()
    {
        var invoice = Issued(80000);
        invoice.AttachPaymentLink(Guid.NewGuid(), "https://pay.example.test/x", Now);
        Assert.True(invoice.Void("mistake", Now, Actor).IsSuccess);
        var documents = new RecordingDocuments();

        await RunAsync(invoice, documents);

        Assert.Null(documents.Sent!.PaymentUrl);
    }

    /// <summary>P6: el PDF dice el día de la oficina; emitida a las 20:00 del 31-dic en Nueva York es del 31 y de 2026.</summary>
    [Fact]
    public async Task A_frozen_invoice_prints_its_office_dates()
    {
        var invoice = Issued(10_000);
        var frozen = Invoice
            .CreateDraft(
                Tenant,
                Actor,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, 10_000, 0)],
                null,
                Now
            )
            .Value;
        frozen.Issue(
            InvoiceNumber.Create("INV-2026-00044").Value,
            new DateTime(2027, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            BuildingBlocks.TimeZones.ResolvedTimeZone.Resolve("America/New_York"),
            Actor
        );
        frozen.ApplyPayment(
            InvoicePaymentSource.Manual,
            "ref",
            Money.Create(10_000, "USD").Value,
            PaymentMethod.Cash,
            new DateTime(2027, 1, 15, 2, 0, 0, DateTimeKind.Utc),
            Actor
        );
        var documents = new RecordingDocuments();

        await RunAsync(frozen, documents);

        var sent = Assert.IsType<InvoiceDocumentRequest>(documents.Sent);
        Assert.Equal(new DateOnly(2026, 12, 31), sent.IssueDate);
        Assert.Equal(new DateOnly(2027, 1, 30), sent.DueDate);
        Assert.Equal(2026, sent.TaxYear);
        Assert.Equal(new DateOnly(2027, 1, 14), sent.PaidDate);
        Assert.NotNull(invoice);
    }

    /// <summary>Una factura emitida antes no tiene fechas congeladas: conserva el día que ya se imprimió (UTC).</summary>
    [Fact]
    public async Task An_invoice_issued_before_keeps_the_dates_it_already_printed()
    {
        var old = Invoice
            .CreateDraft(
                Tenant,
                Actor,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, 10_000, 0)],
                null,
                Now
            )
            .Value;
        var issuedAt = new DateTime(2027, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        old.Issue(InvoiceNumber.Create("INV-2027-00001").Value, issuedAt, issuedAt.AddDays(30), Actor);
        var documents = new RecordingDocuments();

        await RunAsync(old, documents);

        var sent = Assert.IsType<InvoiceDocumentRequest>(documents.Sent);
        Assert.Equal(new DateOnly(2027, 1, 1), sent.IssueDate);
        Assert.Equal(2027, sent.TaxYear);
    }

    private static Invoice Issued(long cents)
    {
        var invoice = Invoice
            .CreateDraft(
                Tenant,
                Actor,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, cents, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00043").Value, Now, Now.AddDays(30), Actor).IsSuccess);
        return invoice;
    }

    private static void Pay(Invoice invoice, long cents) =>
        Assert.True(
            invoice
                .ApplyPayment(
                    InvoicePaymentSource.Manual,
                    Guid.NewGuid().ToString("N"),
                    Money.Create(cents, "USD").Value,
                    PaymentMethod.Cash,
                    Now,
                    Actor
                )
                .IsSuccess
        );

    private static Task RunAsync(Invoice invoice, IInvoiceDocumentClient documents) =>
        GenerateInvoicePdfHandler.Handle(
            new GenerateInvoicePdfCommand(Tenant, invoice.Id),
            new SingleInvoice(invoice),
            documents,
            new FixedCorrelation(),
            CancellationToken.None
        );

    private static async Task<InvoiceDocumentRequest> GenerateAsync(
        IssuerSnapshot? issuer,
        InvoiceTermsSnapshot? terms = null
    )
    {
        var draft = Invoice.CreateDraft(
            Tenant,
            Actor,
            new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", "5551234567", null, null),
            "USD",
            [new DraftInvoiceLine("Tax prep", 1, 35000, 0)],
            null,
            Now,
            issuer,
            terms
        );
        Assert.True(draft.IsSuccess);
        var invoice = draft.Value;
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00042").Value, Now, Now.AddDays(30), Actor).IsSuccess);

        var documents = new RecordingDocuments();
        await GenerateInvoicePdfHandler.Handle(
            new GenerateInvoicePdfCommand(Tenant, invoice.Id),
            new SingleInvoice(invoice),
            documents,
            new FixedCorrelation(),
            CancellationToken.None
        );

        return Assert.IsType<InvoiceDocumentRequest>(documents.Sent);
    }

    private sealed class RecordingDocuments : IInvoiceDocumentClient
    {
        public InvoiceDocumentRequest? Sent { get; private set; }
        public string? Key { get; private set; }

        public Task<Result> GenerateAsync(
            InvoiceDocumentRequest request,
            Guid tenantId,
            string idempotencyKey,
            string? correlationId,
            CancellationToken ct = default
        )
        {
            Sent = request;
            Key = idempotencyKey;
            return Task.FromResult(Result.Success());
        }
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
            Guid? customerId = null,
            OpenInvoicesFilter? open = null
        ) => throw new NotSupportedException();

        public Task AddAsync(Invoice invoice, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test-correlation";

        public IDisposable Push(string correlationId) => new Noop();

        public void Set(string correlationId) { }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
