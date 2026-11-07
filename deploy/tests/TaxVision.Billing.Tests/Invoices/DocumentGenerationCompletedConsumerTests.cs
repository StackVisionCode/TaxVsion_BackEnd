using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>BIL-7: Documents no garantiza el orden. Un PDF de un estado anterior no pisa al vigente.</summary>
public sealed class DocumentGenerationCompletedConsumerTests
{
    private const long TotalCents = 80000;
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task LateNonPaidPdf_DoesNotOverwritePaidPdf()
    {
        var invoice = Issued();
        Pay(invoice, TotalCents);
        var paidPdf = Guid.NewGuid();
        await CompleteAsync(invoice, paidPdf, Key(invoice, "paid:nolink"));

        await CompleteAsync(invoice, Guid.NewGuid(), Key(invoice, $"v1:nolink:due{TotalCents}"));

        Assert.Equal(paidPdf, invoice.PaidPdfFileId);
        Assert.Equal(paidPdf, invoice.PdfFileId);
    }

    [Fact]
    public async Task LateOlderBalancePdf_DoesNotOverwriteNewerBalancePdf()
    {
        var invoice = Issued();
        Pay(invoice, 30000);
        var newerPdf = Guid.NewGuid();
        await CompleteAsync(invoice, newerPdf, Key(invoice, "v1:nolink:due50000"));

        await CompleteAsync(invoice, Guid.NewGuid(), Key(invoice, $"v1:nolink:due{TotalCents}"));

        Assert.Equal(newerPdf, invoice.PdfFileId);
        Assert.Null(invoice.PaidPdfFileId);
    }

    [Fact]
    public async Task LatePaidPdf_DoesNotOverwriteVoidedPdf()
    {
        var invoice = Issued();
        Pay(invoice, TotalCents);
        Assert.True(invoice.Void("mistake", Now, Actor).IsSuccess);
        var voidedPdf = Guid.NewGuid();
        await CompleteAsync(invoice, voidedPdf, Key(invoice, "voided:nolink:due0"));

        await CompleteAsync(invoice, Guid.NewGuid(), Key(invoice, "paid:nolink"));

        Assert.Equal(voidedPdf, invoice.PdfFileId);
    }

    [Fact]
    public async Task OlderPdf_OnInvoiceWithoutPdf_IsAttached()
    {
        var invoice = Issued();
        Pay(invoice, 30000);
        var firstPdf = Guid.NewGuid();

        await CompleteAsync(invoice, firstPdf, Key(invoice, $"v1:nolink:due{TotalCents}"));

        Assert.Equal(firstPdf, invoice.PdfFileId);
    }

    [Fact]
    public async Task PdfWithoutKey_FromOlderDocuments_IsAttached()
    {
        var invoice = Issued();
        var pdf = Guid.NewGuid();

        await CompleteAsync(invoice, pdf, key: null);

        Assert.Equal(pdf, invoice.PdfFileId);
    }

    private static string Key(Invoice invoice, string version) => $"invoice-pdf:{invoice.Id:N}:{version}";

    private static Invoice Issued()
    {
        var invoice = Invoice
            .CreateDraft(
                Tenant,
                Actor,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, TotalCents, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00077").Value, Now, Now.AddDays(30), Actor).IsSuccess);
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

    private static Task CompleteAsync(Invoice invoice, Guid fileId, string? key) =>
        DocumentGenerationCompletedConsumer.Handle(
            new DocumentGenerationCompletedIntegrationEvent
            {
                TenantId = Tenant,
                GenerationId = Guid.NewGuid(),
                DocumentType = "Invoice",
                OwnerType = "Invoice",
                OwnerId = invoice.Id,
                DocumentVersion = 1,
                FileId = fileId,
                FileName = "invoice.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1,
                IdempotencyKey = key,
            },
            new SingleInvoice(invoice),
            new CountingUnitOfWork(),
            new NoopCorrelationContext(),
            TimeProvider.System,
            NullLogger<Invoice>.Instance,
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

        public Task<Invoice?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Invoice?> GetOnboardingInvoiceByIdAsync(Guid invoiceId, CancellationToken ct = default) =>
            Task.FromResult<Invoice?>(null);

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
