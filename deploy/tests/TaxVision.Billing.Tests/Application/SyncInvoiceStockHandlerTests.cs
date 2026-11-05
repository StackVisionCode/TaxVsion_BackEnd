using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Application;

/// <summary>Reconciliación del stock de una factura después del commit (BIL-3, patrón BB-8).</summary>
public sealed class SyncInvoiceStockHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly Guid CatalogItemId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly RecordingInventoryClient _inventory = new();
    private readonly RecordingBillingMetrics _metrics = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Issued_SendsOnlyCatalogLines()
    {
        var invoice = IssuedInvoice();

        await SyncAsync(new InMemoryInvoiceRepository(invoice), invoice.Id);

        var commit = Assert.Single(_inventory.Commits);
        var line = Assert.Single(commit.Lines);
        Assert.Equal((CatalogItemId, 3), (line.CatalogItemId, line.Quantity));
    }

    [Fact]
    public async Task Voided_SendsEmptyLines()
    {
        var invoice = IssuedInvoice();
        Assert.True(invoice.Void("wrong client", Now, ActorUserId).IsSuccess);

        await SyncAsync(new InMemoryInvoiceRepository(invoice), invoice.Id);

        Assert.Empty(Assert.Single(_inventory.Commits).Lines);
    }

    [Fact]
    public async Task Draft_SendsEmptyLines()
    {
        var invoice = Draft();

        await SyncAsync(new InMemoryInvoiceRepository(invoice), invoice.Id);

        Assert.Empty(Assert.Single(_inventory.Commits).Lines);
    }

    [Fact]
    public async Task MissingInvoice_SendsEmptyLines()
    {
        var invoiceId = Guid.NewGuid();

        await SyncAsync(new InMemoryInvoiceRepository(), invoiceId);

        var commit = Assert.Single(_inventory.Commits);
        Assert.Equal(invoiceId, commit.InvoiceId);
        Assert.Empty(commit.Lines);
    }

    [Fact]
    public async Task TransientFailure_Throws()
    {
        var invoice = IssuedInvoice();
        _inventory.CommitResult = Result.Failure(new Error("Billing.Inventory.Unreachable", "down"));

        await Assert.ThrowsAnyAsync<Exception>(() => SyncAsync(new InMemoryInvoiceRepository(invoice), invoice.Id));
        Assert.Equal(0, _metrics.StockSyncInsufficient);
    }

    [Fact]
    public async Task InsufficientStock_RecordsMetricWithoutRetry()
    {
        var invoice = IssuedInvoice();
        _inventory.CommitResult = Result.Failure(new Error("inventory.insufficientStock", "No stock."));

        await SyncAsync(new InMemoryInvoiceRepository(invoice), invoice.Id);

        Assert.Single(_inventory.Commits);
        Assert.Equal(1, _metrics.StockSyncInsufficient);
    }

    [Fact]
    public async Task Success_MarksTheSyncedVersion()
    {
        var invoice = IssuedInvoice();

        await SyncAsync(new InMemoryInvoiceRepository(invoice), invoice.Id);

        Assert.Equal(invoice.UpdatedAtUtc, invoice.StockSyncedAtUtc);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task InsufficientStock_StillMarks_SoTheJobDoesNotRetry()
    {
        var invoice = IssuedInvoice();
        _inventory.CommitResult = Result.Failure(new Error("inventory.insufficientStock", "No stock."));

        await SyncAsync(new InMemoryInvoiceRepository(invoice), invoice.Id);

        Assert.Equal(invoice.UpdatedAtUtc, invoice.StockSyncedAtUtc);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task TransientFailure_DoesNotMark()
    {
        var invoice = IssuedInvoice();
        _inventory.CommitResult = Result.Failure(new Error("Billing.Inventory.Unreachable", "down"));

        await Assert.ThrowsAnyAsync<Exception>(() => SyncAsync(new InMemoryInvoiceRepository(invoice), invoice.Id));

        Assert.Null(invoice.StockSyncedAtUtc);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Fact]
    public async Task FailureAfterInventoryAccepted_DoesNotResend()
    {
        // El reintento del comando (p. ej. falló el ack después de que Inventory aceptó) no mueve stock otra vez.
        var invoice = IssuedInvoice();
        var invoices = new InMemoryInvoiceRepository(invoice);

        await SyncAsync(invoices, invoice.Id);
        await SyncAsync(invoices, invoice.Id);

        Assert.Equal(2, _inventory.Commits.Count);
        Assert.Equal(1, _inventory.Moves);
        Assert.Equal(3, _inventory.Discounted[invoice.Id][CatalogItemId]);
    }

    private Task SyncAsync(InMemoryInvoiceRepository invoices, Guid invoiceId) =>
        SyncInvoiceStockHandler.Handle(
            new SyncInvoiceStockCommand(TenantId, invoiceId),
            invoices,
            _inventory,
            _unitOfWork,
            _metrics,
            NullLogger<SyncInvoiceStockCommand>.Instance,
            CancellationToken.None
        );

    private static Invoice Draft() =>
        Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [
                    new DraftInvoiceLine("Ink cartridge", 3, 2_000, 0, CatalogItemId),
                    new DraftInvoiceLine("Tax preparation", 1, 15_000, 0),
                ],
                null,
                Now
            )
            .Value;

    private static Invoice IssuedInvoice()
    {
        var invoice = Draft();
        Assert.True(invoice.Issue("INV-2026-00001", Now, Now.AddDays(30), ActorUserId).IsSuccess);
        return invoice;
    }
}
