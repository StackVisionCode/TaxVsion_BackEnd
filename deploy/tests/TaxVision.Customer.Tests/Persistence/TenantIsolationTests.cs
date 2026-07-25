using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — cubre el <c>HasQueryFilter</c> global fail-closed de
/// <see cref="CustomerDbContext"/> (safety net EF Core, Layer 3a). Usa <see cref="DomainCustomer"/>
/// como aggregate ITenantOwned representativo — el mecanismo genérico aplica igual a cualquier
/// entidad que herede <c>TenantEntity</c>.
/// </summary>
public sealed class TenantIsolationTests
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

    private static DomainCustomer CreateCustomer(Guid tenantId)
    {
        var name = PersonalName.Create("Ada", "Lovelace").Value;
        var email = EmailAddress.Create($"ada-{Guid.NewGuid():N}@example.com").Value;
        return DomainCustomer
            .Register(
                tenantId,
                CustomerKind.Individual,
                name,
                null,
                email,
                null,
                Language.En,
                PreferredChannel.Email,
                Guid.NewGuid()
            )
            .Value;
    }

    [Fact]
    public async Task Global_query_filter_isolates_tenant_A_from_tenant_B_data()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantContext = new FakeTenantContext();
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.Customers.AddRangeAsync(CreateCustomer(tenantA), CreateCustomer(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.Customers.ToListAsync();

        var customer = Assert.Single(visible);
        Assert.Equal(tenantA, customer.TenantId);
    }

    [Fact]
    public async Task Global_query_filter_returns_empty_when_TenantContext_has_no_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.Customers.AddAsync(CreateCustomer(Guid.NewGuid()));
            await seedDb.SaveChangesAsync();
        }

        // tenantContext nunca se seteó — HasTenant == false, fail-closed compara contra Guid.Empty.
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.Customers.ToListAsync();

        Assert.Empty(visible);
    }

    [Fact]
    public async Task IgnoreQueryFilters_returns_all_tenants_data()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.Customers.AddRangeAsync(CreateCustomer(tenantA), CreateCustomer(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var all = await db.Customers.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, c => c.TenantId == tenantA);
        Assert.Contains(all, c => c.TenantId == tenantB);
    }
}
