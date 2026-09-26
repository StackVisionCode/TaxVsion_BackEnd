using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Persistence;

/// <summary>
/// Cubre el resumen del dashboard (GET /customers/overview): total exacto, altas por mes y últimas
/// altas, todo scopeado al tenant. Reemplaza el "traer 500 filas y agrupar en el cliente" del front.
/// </summary>
public sealed class CustomerOverviewReadServiceTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private sealed class NoopProtector : ISensitiveDataProtector
    {
        public byte[] Protect(string plainText) => System.Text.Encoding.UTF8.GetBytes(plainText);

        public string Unprotect(byte[] cipher) => System.Text.Encoding.UTF8.GetString(cipher);

        public string ComputeBlindIndex(string plainText, Guid tenantId) => plainText;
    }

    private static CustomerDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<CustomerDbContext>().UseInMemoryDatabase(databaseName).Options, tenantContext);

    private static DomainCustomer NewCustomer(Guid tenantId, Guid byUser)
    {
        var name = PersonalName.Create("Grace", "Hopper").Value;
        var email = EmailAddress.Create($"grace-{Guid.NewGuid():N}@example.com").Value;
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
                byUser
            )
            .Value;
    }

    [Fact]
    public async Task GetOverviewAsync_returns_tenant_scoped_total_monthly_and_capped_recent()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var byUser = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            for (var i = 0; i < 4; i++)
                seedDb.Customers.Add(NewCustomer(tenantId, byUser));
            for (var i = 0; i < 2; i++)
                seedDb.Customers.Add(NewCustomer(otherTenant, byUser)); // otro tenant: no debe contar
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, tenantContext);
        var reader = new CustomerReadService(db, new NoopProtector());

        var overview = await reader.GetOverviewAsync(tenantId, months: 6);

        Assert.Equal(4, overview.TotalCount);
        Assert.Equal(3, overview.Recent.Count); // capado a 3
        // Todas las altas cayeron en el mes en curso (CreatedAtUtc = UtcNow).
        var now = DateTime.UtcNow;
        var bucket = Assert.Single(overview.Monthly);
        Assert.Equal(now.Year, bucket.Year);
        Assert.Equal(now.Month, bucket.Month);
        Assert.Equal(4, bucket.Count);
    }

    [Fact]
    public async Task SearchAsync_caps_page_size_at_100()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var byUser = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            for (var i = 0; i < 105; i++)
                seedDb.Customers.Add(NewCustomer(tenantId, byUser));
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, tenantContext);
        var reader = new CustomerReadService(db, new NoopProtector());

        var page = await reader.SearchAsync(tenantId, term: null, CustomerStatusFilter.All, page: 1, size: 1000);

        Assert.Equal(100, page.Items.Count); // capado a 100
        Assert.Equal(100, page.Size);
        Assert.Equal(105, page.TotalCount); // el total exacto NO se capa
    }

    [Fact]
    public async Task GetOverviewAsync_for_empty_tenant_returns_zeros()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        await using var db = CreateContext(databaseName, tenantContext);
        var reader = new CustomerReadService(db, new NoopProtector());

        var overview = await reader.GetOverviewAsync(tenantId, months: 6);

        Assert.Equal(0, overview.TotalCount);
        Assert.Empty(overview.Monthly);
        Assert.Empty(overview.Recent);
    }
}
