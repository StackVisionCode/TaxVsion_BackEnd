using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Domain.Assignments;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Assignments;

/// <summary>
/// La asignación se crea mutando el agregado ya cargado, así que EF tiene que rastrearla como
/// <c>Added</c>: si la fábrica le pone la clave, EF la toma por fila existente y emite UPDATE contra
/// una fila que no existe. Los handlers (AssignPreparer, GrantAccess, BulkAssign) usan repositorios
/// falsos, así que sin este test la escritura real no la cubre nadie.
/// </summary>
public sealed class CustomerAssignmentTrackingTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static CustomerDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<CustomerDbContext>().UseInMemoryDatabase(databaseName).Options, tenantContext);

    private static DomainCustomer NewCustomer(Guid tenantId) =>
        DomainCustomer
            .Register(
                tenantId,
                CustomerKind.Individual,
                PersonalName.Create("Ada", "Lovelace").Value,
                null,
                EmailAddress.Create($"ada-{Guid.NewGuid():N}@example.com").Value,
                null,
                Language.En,
                PreferredChannel.Email,
                Guid.NewGuid()
            )
            .Value;

    [Fact]
    public async Task AssignPreparer_on_a_loaded_customer_inserts_the_assignment()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var preparerUserId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        Guid customerId;
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var customer = NewCustomer(tenantId);
            seedDb.Customers.Add(customer);
            await seedDb.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using (var writeDb = CreateContext(databaseName, tenantContext))
        {
            var customer = await writeDb
                .Customers.IgnoreQueryFilters()
                .Include(c => c.Assignments)
                .FirstAsync(c => c.Id == customerId);

            Assert.True(customer.AssignPreparer(preparerUserId, Guid.NewGuid()).IsSuccess);

            var tracked = writeDb.ChangeTracker.Entries<CustomerAssignment>().Single();
            Assert.Equal(EntityState.Added, tracked.State);

            await writeDb.SaveChangesAsync();
        }

        await using var readDb = CreateContext(databaseName, tenantContext);
        var stored = await readDb.CustomerAssignments.IgnoreQueryFilters().SingleAsync(a => a.CustomerId == customerId);

        Assert.Equal(preparerUserId, stored.UserId);
        Assert.Equal(tenantId, stored.TenantId);
        Assert.True(stored.IsPrimary);
        Assert.NotEqual(Guid.Empty, stored.Id);
    }

    [Fact]
    public async Task GrantAccess_on_a_loaded_customer_inserts_the_assignment()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        Guid customerId;
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var customer = NewCustomer(tenantId);
            seedDb.Customers.Add(customer);
            await seedDb.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using (var writeDb = CreateContext(databaseName, tenantContext))
        {
            var customer = await writeDb
                .Customers.IgnoreQueryFilters()
                .Include(c => c.Assignments)
                .FirstAsync(c => c.Id == customerId);

            Assert.True(customer.GrantAccess(userId, Guid.NewGuid()).IsSuccess);
            await writeDb.SaveChangesAsync();
        }

        await using var readDb = CreateContext(databaseName, tenantContext);
        var stored = await readDb.CustomerAssignments.IgnoreQueryFilters().SingleAsync(a => a.CustomerId == customerId);

        Assert.Equal(userId, stored.UserId);
        Assert.False(stored.IsPrimary);
    }
}
