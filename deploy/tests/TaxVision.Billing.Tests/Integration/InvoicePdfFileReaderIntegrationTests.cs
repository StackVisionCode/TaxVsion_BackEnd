using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// Lectura del backfill contra el SQL Server real: solo facturas del tenant con algún PDF, en páginas estables.
/// Tenants propios; se borran al terminar.
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class InvoicePdfFileReaderIntegrationTests(BillingTestDatabase database) : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly string _connectionString = database.ConnectionString;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var tenantId in new[] { _tenantId, _otherTenantId })
        {
            await using var db = NewContext(tenantId);
            await db.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == tenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Pages_only_invoices_of_the_tenant_that_have_a_pdf()
    {
        var first = WithPdf(_tenantId, "INV-2026-91001");
        var second = WithPdf(_tenantId, "INV-2026-91002");
        var withoutPdf = Issued(_tenantId, "INV-2026-91003");
        var foreign = WithPdf(_otherTenantId, "INV-2026-91004");
        await SeedAsync(_tenantId, first, second, withoutPdf);
        await SeedAsync(_otherTenantId, foreign);

        await using var db = NewContext(_tenantId);
        var reader = new InvoicePdfFileReader(db);
        var page1 = await reader.ListPageAsync(_tenantId, 0, 1);
        var page2 = await reader.ListPageAsync(_tenantId, 1, 1);
        var page3 = await reader.ListPageAsync(_tenantId, 2, 1);

        var rows = page1.Concat(page2).ToList();
        Assert.Empty(page3);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), rows.Select(r => r.InvoiceId).Order());
        var row = rows.Single(r => r.InvoiceId == first.Id);
        Assert.Equal(first.PdfFileId, row.PdfFileId);
        Assert.Null(row.PaidPdfFileId);
        Assert.Equal(_customerId, row.CustomerId);
    }

    [Fact]
    public async Task Finds_by_file_id_only_invoices_of_the_tenant_whose_pdf_is_in_the_list()
    {
        var claimed = WithPdf(_tenantId, "INV-2026-91011");
        var unclaimed = WithPdf(_tenantId, "INV-2026-91012");
        var foreign = WithPdf(_otherTenantId, "INV-2026-91013");
        await SeedAsync(_tenantId, claimed, unclaimed);
        await SeedAsync(_otherTenantId, foreign);

        await using var db = NewContext(_tenantId);
        var rows = await new InvoicePdfFileReader(db).ListByFileIdsAsync(
            _tenantId,
            [claimed.PdfFileId!.Value, foreign.PdfFileId!.Value, Guid.NewGuid()]
        );

        var row = Assert.Single(rows);
        Assert.Equal(claimed.Id, row.InvoiceId);
        Assert.Equal(claimed.PdfFileId, row.PdfFileId);
        Assert.Equal(_customerId, row.CustomerId);
    }

    private Invoice WithPdf(Guid tenantId, string number)
    {
        var invoice = Issued(tenantId, number);
        invoice.AttachPdf(Guid.NewGuid(), null, DateTime.UtcNow);
        return invoice;
    }

    private Invoice Issued(Guid tenantId, string number)
    {
        var invoice = Invoice
            .CreateDraft(
                tenantId,
                Guid.NewGuid(),
                new CustomerSnapshot(_customerId, "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, 35_000, 0, null)],
                null,
                DateTime.UtcNow
            )
            .Value;
        Assert.True(
            invoice
                .Issue(InvoiceNumber.Create(number).Value, DateTime.UtcNow, DateTime.UtcNow.AddDays(30), Guid.NewGuid())
                .IsSuccess
        );
        return invoice;
    }

    private async Task SeedAsync(Guid tenantId, params Invoice[] invoices)
    {
        await using var db = NewContext(tenantId);
        db.Invoices.AddRange(invoices);
        await db.SaveChangesAsync();
    }

    private BillingDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
