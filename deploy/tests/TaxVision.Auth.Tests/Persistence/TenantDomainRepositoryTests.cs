using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Persistence;

/// <summary>
/// Regresión del bug de producción del 2026-07-24: <c>TenantResolver.ResolveAsync</c> (usado en
/// CADA request entrante para determinar el tenant a partir del Host) llama
/// <c>ITenantDomainRepository.GetByHostAsync</c> ANTES de que exista ningún <see cref="ITenantContext"/>
/// — es justamente lo que este método existe para determinar. Sin <c>IgnoreQueryFilters()</c>, el
/// HasQueryFilter global de RBAC Fase 5 (ver <see cref="TenantIsolationTests"/>) hace que la
/// consulta compare contra un tenant vacío y nunca matchee nada, tirando 404 en /auth/login para
/// cualquier subdominio o custom hostname, sin importar que el registro exista y esté Active.
/// </summary>
public sealed class TenantDomainRepositoryTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static AuthDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options,
            new FakeMessageBus(),
            tenantContext
        );

    [Fact]
    public async Task GetByHostAsync_resolves_domain_when_no_tenant_is_set_yet()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();

        var slug = SubdomainSlug.Create("assemble").Value;
        var domain = TenantDomain
            .CreateSubdomain(tenantId, slug, "taxprocore.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.TenantDomains.AddAsync(domain);
            await seedDb.SaveChangesAsync();
        }

        // Simula exactamente el momento de resolución de host en login: ningún tenant
        // ambiental seteado todavía (HasTenant == false), tal como corre TenantResolver.
        await using var db = CreateContext(databaseName, tenantContext);
        var repository = new TenantDomainRepository(db);

        var resolved = await repository.GetByHostAsync("assemble.taxprocore.com");

        Assert.NotNull(resolved);
        Assert.Equal(tenantId, resolved!.TenantId);
    }
}
