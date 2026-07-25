using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;

namespace TaxVision.PaymentClient.Tests.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — cubre el <c>HasQueryFilter</c> global fail-closed de
/// <see cref="PaymentClientDbContext"/> (safety net EF Core, Layer 3a). Usa
/// <see cref="TenantPaymentConfig"/> como aggregate ITenantOwned representativo — el mecanismo
/// genérico aplica igual a <c>TenantPayment</c>/<c>PaymentLink</c>/<c>TenantRecurringPayment</c>.
/// AuditEntries no implementa ITenantOwned, así que el filtro no lo alcanza.
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

    private static PaymentClientDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<PaymentClientDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    private static TenantPaymentConfig CreateConfig(Guid tenantId)
    {
        var descriptor = StatementDescriptor.Create("ACME TAX").Value;
        return TenantPaymentConfig
            .Create(
                tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_123",
                descriptor,
                DateTime.UtcNow
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
            await seedDb.TenantPaymentConfigs.AddRangeAsync(CreateConfig(tenantA), CreateConfig(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.TenantPaymentConfigs.ToListAsync();

        var config = Assert.Single(visible);
        Assert.Equal(tenantA, config.TenantId);
    }

    [Fact]
    public async Task Global_query_filter_returns_empty_when_TenantContext_has_no_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.TenantPaymentConfigs.AddAsync(CreateConfig(Guid.NewGuid()));
            await seedDb.SaveChangesAsync();
        }

        // tenantContext nunca se seteó — HasTenant == false, fail-closed compara contra Guid.Empty.
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.TenantPaymentConfigs.ToListAsync();

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
            await seedDb.TenantPaymentConfigs.AddRangeAsync(CreateConfig(tenantA), CreateConfig(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var all = await db.TenantPaymentConfigs.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, c => c.TenantId == tenantA);
        Assert.Contains(all, c => c.TenantId == tenantB);
    }
}
