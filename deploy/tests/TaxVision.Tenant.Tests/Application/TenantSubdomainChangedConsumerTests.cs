using BuildingBlocks.Caching;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Consumers;
using TaxVision.Tenant.Tests.TestSupport;
using TenantEntity = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>Auth renombró el subdominio primario de una oficina: el registro de oficinas guarda el slug
/// nuevo y el listado de PlatformAdmin deja de servir el viejo desde la caché.</summary>
public sealed class TenantSubdomainChangedConsumerTests
{
    private const string TenantListVersionKey = "tenants:list:v2:version";

    [Fact]
    public async Task Primary_rename_updates_the_slug_saves_once_and_invalidates_the_list_cache()
    {
        var tenant = TenantEntity.Create("Acme Tax Office", "acme-tax", "UTC").Value;
        var repo = new FakeTenantRepository { Existing = tenant };
        var unitOfWork = new CountingUnitOfWork();
        var cache = new RecordingCache();

        await HandleAsync(Event(tenant.Id, isPrimary: true), repo, unitOfWork, cache);

        Assert.Equal("acme-new", tenant.SubDomain);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal([TenantListVersionKey], cache.SetKeys);
    }

    [Fact]
    public async Task Non_primary_rename_leaves_the_tenant_untouched()
    {
        var tenant = TenantEntity.Create("Acme Tax Office", "acme-tax", "UTC").Value;
        var repo = new FakeTenantRepository { Existing = tenant };
        var unitOfWork = new CountingUnitOfWork();
        var cache = new RecordingCache();

        await HandleAsync(Event(tenant.Id, isPrimary: false), repo, unitOfWork, cache);

        Assert.Equal("acme-tax", tenant.SubDomain);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(cache.SetKeys);
    }

    [Fact]
    public async Task Unknown_tenant_is_a_noop()
    {
        var repo = new FakeTenantRepository();
        var unitOfWork = new CountingUnitOfWork();
        var cache = new RecordingCache();

        await HandleAsync(Event(Guid.NewGuid(), isPrimary: true), repo, unitOfWork, cache);

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(cache.SetKeys);
    }

    private static Task HandleAsync(
        TenantSubdomainChangedIntegrationEvent evt,
        ITenantRepository repo,
        CountingUnitOfWork unitOfWork,
        ICacheService cache
    ) =>
        TenantSubdomainChangedConsumer.Handle(
            evt,
            repo,
            unitOfWork,
            cache,
            new NoopCorrelationContext(),
            NullLogger<TenantSubdomainChangedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private static TenantSubdomainChangedIntegrationEvent Event(Guid tenantId, bool isPrimary) =>
        new()
        {
            TenantId = tenantId,
            DomainId = Guid.NewGuid(),
            OldHost = "acme-tax.taxproffice.com",
            NewHost = "acme-new.taxproffice.com",
            OldSlug = "acme-tax",
            NewSlug = "acme-new",
            IsPrimary = isPrimary,
        };

    private sealed class FakeTenantRepository : ITenantRepository
    {
        public TenantEntity? Existing { get; set; }

        public Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Existing is not null && Existing.Id == id ? Existing : null);

        public Task AddAsync(TenantEntity entity, CancellationToken ct = default) => throw new NotSupportedException();

        public void Remove(TenantEntity entity) => throw new NotSupportedException();

        public Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Guid?> GetIdBySubDomainAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantEntity?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>TenantListCache.InvalidateAsync rota la versión del listado con un SetAsync; se registra la clave.</summary>
    private sealed class RecordingCache : ICacheService
    {
        public List<string> SetKeys { get; } = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            SetKeys.Add(key);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }
}
