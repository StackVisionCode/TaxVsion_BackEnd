using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.ResyncInvoiceArtifacts;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Application;

/// <summary>Reposición de BIL-BB2: una factura emitida vuelve a pedir su enlace (y, encadenado, su PDF).</summary>
public sealed class ResyncInvoiceArtifactsHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task IssuedInvoice_RepublishesEnsurePaymentLink()
    {
        var invoice = Draft();
        Assert.True(invoice.Issue("INV-2026-00001", Now, Now.AddDays(30), Guid.NewGuid()).IsSuccess);
        var bus = new FakeMessageBus();

        var result = await Handle(invoice, bus);

        Assert.True(result.IsSuccess);
        Assert.Equal(new EnsureInvoicePaymentLinkCommand(invoice.TenantId, invoice.Id), Assert.Single(bus.Published));
        Assert.Equal(invoice.TenantId.ToString(), bus.TenantId);
    }

    [Fact]
    public async Task DraftInvoice_IsRejectedWithoutPublishing()
    {
        var invoice = Draft();
        var bus = new FakeMessageBus();

        var result = await Handle(invoice, bus);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.NotIssued", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    private static Task<BuildingBlocks.Results.Result> Handle(Invoice invoice, FakeMessageBus bus) =>
        ResyncInvoiceArtifactsHandler.Handle(
            new ResyncInvoiceArtifactsCommand(invoice.TenantId, invoice.Id),
            new InMemoryInvoiceRepository(invoice),
            bus,
            CancellationToken.None
        );

    private static Invoice Draft() =>
        Invoice
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 10_000, 0)],
                null,
                Now
            )
            .Value;
}
