using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Persistence;

namespace TaxVision.PaymentApp.Tests.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — cubre el <c>HasQueryFilter</c> global fail-closed de
/// <see cref="PaymentAppDbContext"/> (safety net EF Core, Layer 3a). Usa
/// <see cref="TenantProviderCustomer"/> como aggregate ITenantOwned representativo — el mecanismo
/// genérico aplica igual a <c>SaaSPayment</c>/<c>Tenant</c>. AuditEntries/WebhookEvents no
/// implementan ITenantOwned, así que el filtro no los alcanza.
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

    private static PaymentAppDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<PaymentAppDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    private static TenantProviderCustomer CreateCustomer(Guid tenantId)
    {
        var reference = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, $"cus_{Guid.NewGuid():N}").Value;
        return TenantProviderCustomer
            .Register(tenantId, PaymentProviderCode.Stripe, reference, "billing@example.com", DateTime.UtcNow)
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
            await seedDb.TenantProviderCustomers.AddRangeAsync(CreateCustomer(tenantA), CreateCustomer(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.TenantProviderCustomers.ToListAsync();

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
            await seedDb.TenantProviderCustomers.AddAsync(CreateCustomer(Guid.NewGuid()));
            await seedDb.SaveChangesAsync();
        }

        // tenantContext nunca se seteó — HasTenant == false, fail-closed compara contra Guid.Empty.
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.TenantProviderCustomers.ToListAsync();

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
            await seedDb.TenantProviderCustomers.AddRangeAsync(CreateCustomer(tenantA), CreateCustomer(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var all = await db.TenantProviderCustomers.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, c => c.TenantId == tenantA);
        Assert.Contains(all, c => c.TenantId == tenantB);
    }
}
