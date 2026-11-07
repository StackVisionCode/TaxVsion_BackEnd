using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// BIL-3b contra el SQL Server real: el prefiltro del job trae las facturas desalineadas de la ventana y
/// deja afuera las alineadas, los borradores y las de antes del corte. Las fechas de las facturas de prueba
/// son de 2001, así que la ventana no toca facturas reales.
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class InvoiceSyncCandidateQueryIntegrationTests(BillingTestDatabase database) : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly DateTime _changed = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(
        Random.Shared.Next(0, 500_000)
    );

    private readonly string _connectionString = database.ConnectionString;

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

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task ReturnsMisaligned_SkipsAlignedDraftsAndOldOnes()
    {
        var neverSynced = Issued(_changed, "INV-2001-00001");
        var aligned = Issued(_changed, "INV-2001-00002");
        aligned.MarkPaymentLinkSynced(aligned.AmountDue);
        aligned.MarkStockSynced(aligned.UpdatedAtUtc);
        var staleStock = Issued(_changed, "INV-2001-00003");
        staleStock.MarkPaymentLinkSynced(staleStock.AmountDue);
        staleStock.MarkStockSynced(staleStock.UpdatedAtUtc.AddMinutes(-5));
        var oldDue = Issued(_changed, "INV-2001-00004");
        oldDue.MarkPaymentLinkSynced(Money.Create(1, "USD").Value);
        oldDue.MarkStockSynced(oldDue.UpdatedAtUtc);
        var draft = Draft(_changed);
        var beforeCutoff = Issued(_changed.AddDays(-1), "INV-2001-00005");

        await using (var db = NewContext())
        {
            db.Invoices.AddRange(neverSynced, aligned, staleStock, oldDue, draft, beforeCutoff);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var candidates = await new InvoiceSyncCandidateQuery(read).ListAsync(
            _changed.AddMinutes(-1),
            _changed.AddMinutes(1),
            take: 100
        );

        var ids = candidates.Where(i => i.TenantId == _tenantId).Select(i => i.Id).ToHashSet();
        Assert.Equal(new HashSet<Guid> { neverSynced.Id, staleStock.Id, oldDue.Id }, ids);
    }

    private Invoice Draft(DateTime at) =>
        Invoice
            .CreateDraft(
                _tenantId,
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Ink cartridge", 2, 2_000, 0, Guid.NewGuid())],
                null,
                at
            )
            .Value;

    private Invoice Issued(DateTime at, string number)
    {
        var invoice = Draft(at);
        Assert.True(invoice.Issue(InvoiceNumber.Create(number).Value, at, at.AddDays(30), Guid.NewGuid()).IsSuccess);
        return invoice;
    }
}
