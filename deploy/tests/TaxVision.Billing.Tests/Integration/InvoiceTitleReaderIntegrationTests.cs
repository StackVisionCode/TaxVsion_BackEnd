using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// Títulos en vivo contra el SQL Server real: mismo filtro de asignación que la lectura de facturas, sin borradas
/// ni de otro tenant. Tenants propios; se borran al terminar.
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class InvoiceTitleReaderIntegrationTests(BillingTestDatabase database) : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _assignedCustomer = Guid.NewGuid();
    private readonly Guid _viewer = Guid.NewGuid();
    private readonly string _connectionString = database.ConnectionString;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var tenantId in new[] { _tenantId, _otherTenantId })
        {
            await using var db = NewContext(tenantId);
            await db.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == tenantId).ExecuteDeleteAsync();
            await db
                .CustomerAssignmentProjections.IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Reads_only_visible_invoices_of_the_tenant_with_their_number()
    {
        var assigned = Issued(_tenantId, _assignedCustomer, "INV-2026-90001");
        var unassigned = Issued(_tenantId, Guid.NewGuid(), "INV-2026-90002");
        var draft = Draft(_tenantId, _assignedCustomer);
        var deleted = Draft(_tenantId, _assignedCustomer);
        Assert.True(deleted.SoftDeleteDraft(DateTime.UtcNow, Guid.NewGuid()).IsSuccess);
        var foreign = Issued(_otherTenantId, _assignedCustomer, "INV-2026-90003");
        await SeedAsync(_tenantId, assigned, unassigned, draft, deleted);
        await SeedAsync(_otherTenantId, foreign);
        await using (var db = NewContext(_tenantId))
        {
            db.CustomerAssignmentProjections.Add(
                CustomerAssignmentProjection.Create(_tenantId, _assignedCustomer, _viewer, DateTime.UtcNow)
            );
            await db.SaveChangesAsync();
        }
        Guid[] ids = [assigned.Id, unassigned.Id, draft.Id, deleted.Id, foreign.Id, Guid.NewGuid()];

        var restricted = await ReadAsync(ids, assignedTo: _viewer);
        var all = await ReadAsync(ids, assignedTo: null);

        Assert.Equal(new[] { assigned.Id, draft.Id }.Order(), restricted.Select(r => r.Id).Order());
        Assert.Equal(new[] { assigned.Id, unassigned.Id, draft.Id }.Order(), all.Select(r => r.Id).Order());
        Assert.Equal("INV-2026-90001", restricted.Single(r => r.Id == assigned.Id).InvoiceNumber);
        Assert.Null(restricted.Single(r => r.Id == draft.Id).InvoiceNumber);
    }

    // ----- helpers -----

    private async Task<IReadOnlyList<InvoiceTitleRow>> ReadAsync(IReadOnlyCollection<Guid> ids, Guid? assignedTo)
    {
        await using var db = NewContext(_tenantId);
        return await new InvoiceTitleReader(db).ListAsync(_tenantId, ids, assignedTo);
    }

    private static Invoice Draft(Guid tenantId, Guid customerId) =>
        Invoice
            .CreateDraft(
                tenantId,
                Guid.NewGuid(),
                new CustomerSnapshot(customerId, "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, 35_000, 0, null)],
                null,
                DateTime.UtcNow
            )
            .Value;

    private static Invoice Issued(Guid tenantId, Guid customerId, string number)
    {
        var invoice = Draft(tenantId, customerId);
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
