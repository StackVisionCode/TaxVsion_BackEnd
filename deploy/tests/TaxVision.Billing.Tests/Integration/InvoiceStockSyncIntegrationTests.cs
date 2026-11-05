using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Application.Invoices.VoidInvoice;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (BILLING_DB_CONNECTION o user-secrets de Billing): una anulación que falla al
/// guardar no debe haber tocado Inventory. El stock se repone después, con SyncInvoiceStockCommand (BIL-3).
/// </summary>
public sealed class InvoiceStockSyncIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _catalogItemId = Guid.NewGuid();
    private Guid _invoiceId;

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("BILLING_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-billing000001")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set BILLING_DB_CONNECTION or the Billing user-secrets to run this test.");

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private BillingDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public async Task InitializeAsync()
    {
        var invoice = Invoice
            .CreateDraft(
                _tenantId,
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Ink cartridge", 2, 2_000, 0, _catalogItemId)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue("INV-2026-00001", Now, Now.AddDays(30), Guid.NewGuid()).IsSuccess);
        _invoiceId = invoice.Id;

        await using var db = NewContext();
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task VoidFailsAfterSave_InventoryNotCalled()
    {
        var inventory = new RecordingInventoryClient();

        // Otro contexto cambia la factura entre la lectura y el guardado: el RowVersion choca.
        await using var failing = NewContext();
        var stale = await new InvoiceRepository(failing).GetByIdAsync(_tenantId, _invoiceId);
        Assert.NotNull(stale);
        await using (var other = NewContext())
            await other
                .Invoices.IgnoreQueryFilters()
                .Where(i => i.Id == _invoiceId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Notes, "changed elsewhere"));

        await Assert.ThrowsAsync<ConflictException>(() => VoidAsync(failing, new FakeMessageBus()));
        Assert.Empty(inventory.Commits);

        // Reintento: la anulación se guarda y deja el pedido de reponer stock en el outbox.
        await using var retry = NewContext();
        var bus = new FakeMessageBus();
        var voided = await VoidAsync(retry, bus);
        Assert.True(voided.IsSuccess);
        Assert.Empty(inventory.Commits);
        var sync = Assert.Single(bus.Published.OfType<SyncInvoiceStockCommand>());

        await using var worker = NewContext();
        await SyncInvoiceStockHandler.Handle(
            sync,
            new InvoiceRepository(worker),
            inventory,
            worker,
            new RecordingBillingMetrics(),
            NullLogger<SyncInvoiceStockCommand>.Instance,
            CancellationToken.None
        );

        var commit = Assert.Single(inventory.Commits);
        Assert.Equal(_invoiceId, commit.InvoiceId);
        Assert.Empty(commit.Lines);
    }

    private Task<Result> VoidAsync(BillingDbContext db, FakeMessageBus bus) =>
        VoidInvoiceHandler.Handle(
            new VoidInvoiceCommand(_tenantId, _invoiceId, Guid.NewGuid(), "wrong client", CanViewAll: true),
            new InvoiceRepository(db),
            Options.Create(new BillingVisibilityOptions()),
            db,
            bus,
            new NoopCorrelationContext(),
            new FixedClock(Now),
            CancellationToken.None
        );
}
