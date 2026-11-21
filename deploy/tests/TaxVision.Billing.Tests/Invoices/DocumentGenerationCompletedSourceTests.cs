using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>Billing declara en CloudStorage el PDF que la factura adopta y degrada el que deja de ser vigente.</summary>
public sealed class DocumentGenerationCompletedSourceTests
{
    private const long TotalCents = 80000;
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task AttachedPdf_IsDeclaredAsInvoicePdf_WithTheCustomer()
    {
        var invoice = Issued();
        var pdf = Guid.NewGuid();
        var bus = new FakeMessageBus();

        await CompleteAsync(invoice, pdf, Key(invoice, $"v1:nolink:due{TotalCents}"), bus);

        var declared = Assert.Single(Declarations(bus));
        Assert.Equal(pdf, declared.FileId);
        Assert.Equal("billing", declared.SourceService);
        Assert.Equal("invoice", declared.SourceResourceType);
        Assert.Equal(invoice.Id, declared.SourceResourceId);
        Assert.Equal("invoice_pdf", declared.SourceRole);
        Assert.Equal(Customer, declared.RelatedCustomerId);
        Assert.Equal(Tenant, declared.TenantId);
    }

    [Fact]
    public async Task PdfThatReplacesAnother_DeclaresThePreviousAsSuperseded()
    {
        var invoice = Issued();
        var first = Guid.NewGuid();
        await CompleteAsync(invoice, first, Key(invoice, $"v1:nolink:due{TotalCents}"), new FakeMessageBus());
        Pay(invoice, 30000);
        var second = Guid.NewGuid();
        var bus = new FakeMessageBus();

        await CompleteAsync(invoice, second, Key(invoice, "v1:nolink:due50000"), bus);

        var declarations = Declarations(bus);
        Assert.Equal(2, declarations.Count);
        Assert.Contains(declarations, d => d.FileId == second && d.SourceRole == "invoice_pdf");
        Assert.Contains(
            declarations,
            d => d.FileId == first && d.SourceRole == "invoice_pdf_superseded" && d.RelatedCustomerId == Customer
        );
    }

    [Fact]
    public async Task StalePdf_DiscardedByTheInvoice_IsNotDeclared()
    {
        var invoice = Issued();
        Pay(invoice, 30000);
        await CompleteAsync(invoice, Guid.NewGuid(), Key(invoice, "v1:nolink:due50000"), new FakeMessageBus());
        var bus = new FakeMessageBus();

        await CompleteAsync(invoice, Guid.NewGuid(), Key(invoice, $"v1:nolink:due{TotalCents}"), bus);

        Assert.Empty(Declarations(bus));
    }

    [Fact]
    public async Task PaidPdf_IsNotSuperseded_WhileItIsThePaidPdf()
    {
        var invoice = Issued();
        Pay(invoice, TotalCents);
        var paidPdf = Guid.NewGuid();
        await CompleteAsync(invoice, paidPdf, Key(invoice, "paid:nolink"), new FakeMessageBus());
        Assert.True(invoice.Void("mistake", Now, Actor).IsSuccess);
        var voidedPdf = Guid.NewGuid();
        var bus = new FakeMessageBus();

        await CompleteAsync(invoice, voidedPdf, Key(invoice, "voided:nolink:due0"), bus);

        var declared = Assert.Single(Declarations(bus));
        Assert.Equal(voidedPdf, declared.FileId);
        Assert.Equal("invoice_pdf", declared.SourceRole);
        Assert.Equal(paidPdf, invoice.PaidPdfFileId);
    }

    [Fact]
    public async Task Declarations_ArePublishedAfterSaving()
    {
        var invoice = Issued();
        var bus = new FakeMessageBus();
        var unitOfWork = new PublishCountingUnitOfWork(bus);

        await CompleteAsync(invoice, Guid.NewGuid(), Key(invoice, $"v1:nolink:due{TotalCents}"), bus, unitOfWork);

        Assert.Equal([0], unitOfWork.PublishedAtSave);
        Assert.Single(Declarations(bus));
    }

    private static List<FileSourceDeclaredIntegrationEvent> Declarations(FakeMessageBus bus) =>
        bus.Published.OfType<FileSourceDeclaredIntegrationEvent>().ToList();

    private static string Key(Invoice invoice, string version) => $"invoice-pdf:{invoice.Id:N}:{version}";

    private static Invoice Issued()
    {
        var invoice = Invoice
            .CreateDraft(
                Tenant,
                Actor,
                new CustomerSnapshot(Customer, "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, TotalCents, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00078").Value, Now, Now.AddDays(30), Actor).IsSuccess);
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

    private static Task CompleteAsync(
        Invoice invoice,
        Guid fileId,
        string? key,
        FakeMessageBus bus,
        IUnitOfWork? unitOfWork = null
    ) =>
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
            new InMemoryInvoiceRepository(invoice),
            unitOfWork ?? new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            TimeProvider.System,
            NullLogger<Invoice>.Instance,
            CancellationToken.None
        );

    // Anota cuántos mensajes se habían publicado al guardar.
    private sealed class PublishCountingUnitOfWork(FakeMessageBus bus) : IUnitOfWork
    {
        public List<int> PublishedAtSave { get; } = [];

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            PublishedAtSave.Add(bus.Published.Count);
            return Task.FromResult(1);
        }
    }
}
