using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Infrastructure.Tenancy;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Fase A3 — resolución Host -> tenant candidato. "Host desconocido -> 404" se
/// cubre aquí como Unresolved(HostUnknown); el 404 real lo produce
/// TenantHostResolutionMiddleware a partir de este resultado.
/// </summary>
public sealed class TenantResolverTests
{
    private sealed class FakeTenantDomainRepository : ITenantDomainRepository
    {
        public TenantDomain? DomainToReturn { get; set; }

        public Task<TenantDomain?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(DomainToReturn);

        public Task<TenantDomain?> GetByHostAsync(string host, CancellationToken ct = default) =>
            Task.FromResult(DomainToReturn?.Host == host ? DomainToReturn : null);

        public Task<TenantDomain?> GetSubdomainBySlugAsync(string slug, CancellationToken ct = default) =>
            Task.FromResult(
                DomainToReturn?.DomainType == TenantDomainType.Subdomain && DomainToReturn.SubdomainSlug == slug
                    ? DomainToReturn
                    : null
            );

        public Task<IReadOnlyList<TenantDomain>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantDomain>>([]);

        public Task<IReadOnlyList<string>> GetActiveHostsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) => Task.FromResult(false);

        public Task<bool> HostExistsAsync(string host, CancellationToken ct = default) => Task.FromResult(false);

        public Task AddAsync(TenantDomain domain, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TenantDomain>> GetProvisioningCustomHostnamesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantDomain>>([]);
    }

    private sealed class FakeTenantRegistry : ITenantRegistry
    {
        public Tenant? TenantToReturn { get; set; }

        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(TenantToReturn);

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetBillingBlockedAsync(
            Guid tenantId,
            bool blocked,
            string? reason,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class FakeTenantResolutionCache : ITenantResolutionCache
    {
        private readonly Dictionary<string, Guid> _entries = [];

        public Task<Guid?> TryGetAsync(string host, CancellationToken ct = default) =>
            Task.FromResult(_entries.TryGetValue(host, out var tenantId) ? tenantId : (Guid?)null);

        public Task SetAsync(string host, Guid tenantId, CancellationToken ct = default)
        {
            _entries[host] = tenantId;
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(string host, CancellationToken ct = default)
        {
            _entries.Remove(host);
            return Task.CompletedTask;
        }
    }

    private static readonly FixedPlatformSettings Platform = FixedPlatformSettings.For("taxproffice.com");

    private static TenantDomain ActiveDomain(Guid tenantId, string host) =>
        TenantDomain
            .CreateSubdomain(
                tenantId,
                SubdomainSlug.Create(host.Split('.')[0]).Value,
                string.Join('.', host.Split('.')[1..]),
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public async Task Known_active_host_resolves_to_its_tenant()
    {
        var tenantId = Guid.NewGuid();
        var domains = new FakeTenantDomainRepository
        {
            DomainToReturn = ActiveDomain(tenantId, "oficina1.taxproffice.com"),
        };
        var tenants = new FakeTenantRegistry
        {
            TenantToReturn = Tenant.Register(tenantId, "Oficina 1", "oficina1", TenantKind.Customer, "Etc/UTC").Value,
        };
        var resolver = new TenantResolver(domains, tenants, new FakeTenantResolutionCache(), Platform);

        var result = await resolver.ResolveAsync("oficina1.taxproffice.com");

        Assert.True(result.IsResolved);
        Assert.Equal(tenantId, result.TenantId);
    }

    [Fact]
    public async Task Unknown_host_never_resolves_to_a_default_tenant()
    {
        var domains = new FakeTenantDomainRepository(); // sin dominio configurado
        var resolver = new TenantResolver(domains, new FakeTenantRegistry(), new FakeTenantResolutionCache(), Platform);

        var result = await resolver.ResolveAsync("no-existe.taxproffice.com");

        Assert.False(result.IsResolved);
        Assert.Equal(TenantResolutionFailureReason.HostUnknown, result.FailureReason);
    }

    [Fact]
    public async Task Missing_host_is_unresolved()
    {
        var resolver = new TenantResolver(
            new FakeTenantDomainRepository(),
            new FakeTenantRegistry(),
            new FakeTenantResolutionCache(),
            Platform
        );

        var result = await resolver.ResolveAsync(null);

        Assert.False(result.IsResolved);
        Assert.Equal(TenantResolutionFailureReason.HostMissing, result.FailureReason);
    }

    [Fact]
    public async Task Host_of_an_inactive_tenant_is_unresolved()
    {
        var tenantId = Guid.NewGuid();
        var domains = new FakeTenantDomainRepository
        {
            DomainToReturn = ActiveDomain(tenantId, "oficina1.taxproffice.com"),
        };
        var inactiveTenant = Tenant.Register(tenantId, "Oficina 1", "oficina1", TenantKind.Customer, "Etc/UTC").Value;
        inactiveTenant.Deactivate();
        var tenants = new FakeTenantRegistry { TenantToReturn = inactiveTenant };
        var resolver = new TenantResolver(domains, tenants, new FakeTenantResolutionCache(), Platform);

        var result = await resolver.ResolveAsync("oficina1.taxproffice.com");

        Assert.False(result.IsResolved);
        Assert.Equal(TenantResolutionFailureReason.TenantInactive, result.FailureReason);
    }

    [Fact]
    public async Task Host_lookup_is_case_insensitive()
    {
        var tenantId = Guid.NewGuid();
        var domains = new FakeTenantDomainRepository
        {
            DomainToReturn = ActiveDomain(tenantId, "oficina1.taxproffice.com"),
        };
        var tenants = new FakeTenantRegistry
        {
            TenantToReturn = Tenant.Register(tenantId, "Oficina 1", "oficina1", TenantKind.Customer, "Etc/UTC").Value,
        };
        var resolver = new TenantResolver(domains, tenants, new FakeTenantResolutionCache(), Platform);

        var result = await resolver.ResolveAsync("OFICINA1.taxproffice.com");

        Assert.True(result.IsResolved);
        Assert.Equal(tenantId, result.TenantId);
    }

    [Fact]
    public async Task Resolved_host_is_cached_for_subsequent_calls()
    {
        var tenantId = Guid.NewGuid();
        var domains = new FakeTenantDomainRepository
        {
            DomainToReturn = ActiveDomain(tenantId, "oficina1.taxproffice.com"),
        };
        var tenants = new FakeTenantRegistry
        {
            TenantToReturn = Tenant.Register(tenantId, "Oficina 1", "oficina1", TenantKind.Customer, "Etc/UTC").Value,
        };
        var cache = new FakeTenantResolutionCache();
        var resolver = new TenantResolver(domains, tenants, cache, Platform);

        await resolver.ResolveAsync("oficina1.taxproffice.com");
        domains.DomainToReturn = null; // simula que la BD ya no respondería esto — el cache debe seguir sirviendo

        var result = await resolver.ResolveAsync("oficina1.taxproffice.com");

        Assert.True(result.IsResolved);
        Assert.Equal(tenantId, result.TenantId);
    }

    [Fact]
    public async Task An_office_resolves_by_slug_under_the_current_domain_even_if_its_stored_host_is_old()
    {
        var tenantId = Guid.NewGuid();
        var domains = new FakeTenantDomainRepository
        {
            // Fila todavía no re-apuntada: guarda el dominio anterior.
            DomainToReturn = ActiveDomain(tenantId, "oficina1.taxproffice.com"),
        };
        var tenants = new FakeTenantRegistry
        {
            TenantToReturn = Tenant.Register(tenantId, "Oficina 1", "oficina1", TenantKind.Customer, "Etc/UTC").Value,
        };
        var resolver = new TenantResolver(
            domains,
            tenants,
            new FakeTenantResolutionCache(),
            FixedPlatformSettings.For("taxoffix.io")
        );

        var result = await resolver.ResolveAsync("oficina1.taxoffix.io");

        Assert.True(result.IsResolved);
        Assert.Equal(tenantId, result.TenantId);
    }

    [Fact]
    public async Task A_host_under_the_previous_domain_no_longer_resolves()
    {
        var tenantId = Guid.NewGuid();
        var domains = new FakeTenantDomainRepository
        {
            DomainToReturn = ActiveDomain(tenantId, "oficina1.taxproffice.com"),
        };
        var tenants = new FakeTenantRegistry
        {
            TenantToReturn = Tenant.Register(tenantId, "Oficina 1", "oficina1", TenantKind.Customer, "Etc/UTC").Value,
        };
        var resolver = new TenantResolver(
            domains,
            tenants,
            new FakeTenantResolutionCache(),
            FixedPlatformSettings.For("taxoffix.io")
        );

        var result = await resolver.ResolveAsync("oficina1.taxproffice.com");

        Assert.False(result.IsResolved);
        Assert.Equal(TenantResolutionFailureReason.HostUnknown, result.FailureReason);
    }

    [Fact]
    public async Task The_api_host_resolves_to_the_platform_tenant_without_a_domain_row()
    {
        var tenants = new FakeTenantRegistry
        {
            TenantToReturn = Tenant
                .Register(PlatformTenant.Id, "Platform", "platform-internal", TenantKind.Platform, "Etc/UTC")
                .Value,
        };
        var resolver = new TenantResolver(
            new FakeTenantDomainRepository(),
            tenants,
            new FakeTenantResolutionCache(),
            FixedPlatformSettings.For("taxoffix.io")
        );

        var result = await resolver.ResolveAsync("api.taxoffix.io");

        Assert.True(result.IsResolved);
        Assert.Equal(PlatformTenant.Id, result.TenantId);
    }

    [Fact]
    public async Task An_office_custom_hostname_resolves_by_its_exact_host()
    {
        var tenantId = Guid.NewGuid();
        var custom = TenantDomain
            .CreateCustomHostname(tenantId, "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        custom.MarkActive(DateTime.UtcNow);
        var tenants = new FakeTenantRegistry
        {
            TenantToReturn = Tenant.Register(tenantId, "Oficina 1", "oficina1", TenantKind.Customer, "Etc/UTC").Value,
        };
        var resolver = new TenantResolver(
            new FakeTenantDomainRepository { DomainToReturn = custom },
            tenants,
            new FakeTenantResolutionCache(),
            Platform
        );

        var result = await resolver.ResolveAsync("archivos.suoficina.com");

        Assert.True(result.IsResolved);
        Assert.Equal(tenantId, result.TenantId);
    }
}
