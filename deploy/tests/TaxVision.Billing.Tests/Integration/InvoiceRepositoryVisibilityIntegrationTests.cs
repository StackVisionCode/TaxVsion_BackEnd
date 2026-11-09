using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// BIL-2 contra el SQL Server real: la lectura por Id respeta la asignación (P2). Factura del cliente C
/// asignado al usuario A: B no la ve, A sí, y sin alcance (null) también. Tenant propio; se borra al terminar.
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class InvoiceRepositoryVisibilityIntegrationTests(BillingTestDatabase database) : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _assignedUser = Guid.NewGuid();
    private readonly Guid _otherUser = Guid.NewGuid();
    private Guid _invoiceId;

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

    public async Task InitializeAsync()
    {
        var invoice = Invoice
            .CreateDraft(
                _tenantId,
                Guid.NewGuid(),
                new CustomerSnapshot(_customerId, "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, 35_000, 0, null)],
                null,
                DateTime.UtcNow
            )
            .Value;
        _invoiceId = invoice.Id;

        await using var db = NewContext();
        db.Invoices.Add(invoice);
        db.CustomerAssignmentProjections.Add(
            CustomerAssignmentProjection.Create(_tenantId, _customerId, _assignedUser, DateTime.UtcNow)
        );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == _tenantId).ExecuteDeleteAsync();
        await db
            .CustomerAssignmentProjections.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GetById_EmployeeWithoutAssignment_ReturnsNull()
    {
        await using var db = NewContext();
        var repository = new InvoiceRepository(db);

        Assert.Null(await repository.GetByIdAsync(_tenantId, _invoiceId, assignedToUserId: _otherUser));
        Assert.NotNull(await repository.GetByIdAsync(_tenantId, _invoiceId, assignedToUserId: _assignedUser));
        Assert.NotNull(await repository.GetByIdAsync(_tenantId, _invoiceId));
    }
}
