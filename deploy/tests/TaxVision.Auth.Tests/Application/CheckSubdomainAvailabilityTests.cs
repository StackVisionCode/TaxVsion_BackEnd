using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains.Queries;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase A4 — GET check-availability: nunca falla, siempre { available, reason? }.</summary>
public sealed class CheckSubdomainAvailabilityTests
{
    private sealed class FakeTenantDomainRepository : ITenantDomainRepository
    {
        public bool SlugTaken { get; set; }

        public Task<TenantDomain?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<TenantDomain?>(null);

        public Task<TenantDomain?> GetByHostAsync(string host, CancellationToken ct = default) =>
            Task.FromResult<TenantDomain?>(null);

        public Task<IReadOnlyList<TenantDomain>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantDomain>>([]);

        public Task<IReadOnlyList<string>> GetActiveHostsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) => Task.FromResult(SlugTaken);

        public Task<bool> HostExistsAsync(string host, CancellationToken ct = default) => Task.FromResult(false);

        public Task AddAsync(TenantDomain domain, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TenantDomain>> GetProvisioningCustomHostnamesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantDomain>>([]);
    }

    private sealed class FakeReservationRepository : ITenantSubdomainReservationRepository
    {
        public TenantSubdomainReservation? ActiveReservation { get; set; }

        public Task<TenantSubdomainReservation?> GetActiveBySlugAsync(
            string slug,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Task.FromResult(ActiveReservation);

        public Task AddAsync(TenantSubdomainReservation reservation, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task Malformed_slug_is_unavailable_with_the_validation_reason()
    {
        var result = await CheckSubdomainAvailabilityHandler.Handle(
            new CheckSubdomainAvailabilityQuery("ab"),
            new FakeTenantDomainRepository(),
            new FakeReservationRepository(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Available);
        Assert.Equal("TenantDomain.SlugLength", result.Value.Reason);
    }

    [Fact]
    public async Task Reserved_word_is_unavailable()
    {
        var result = await CheckSubdomainAvailabilityHandler.Handle(
            new CheckSubdomainAvailabilityQuery("admin"),
            new FakeTenantDomainRepository(),
            new FakeReservationRepository(),
            CancellationToken.None
        );

        Assert.False(result.Value.Available);
        Assert.Equal("TenantDomain.SlugReserved", result.Value.Reason);
    }

    [Fact]
    public async Task Slug_already_claimed_by_a_tenant_domain_is_unavailable()
    {
        var domains = new FakeTenantDomainRepository { SlugTaken = true };

        var result = await CheckSubdomainAvailabilityHandler.Handle(
            new CheckSubdomainAvailabilityQuery("oficina1"),
            domains,
            new FakeReservationRepository(),
            CancellationToken.None
        );

        Assert.False(result.Value.Available);
        Assert.Equal("TenantDomain.SlugTaken", result.Value.Reason);
    }

    [Fact]
    public async Task Slug_with_an_active_temporary_reservation_is_unavailable()
    {
        var reservation = TenantSubdomainReservation
            .Create(
                SubdomainSlug.Create("oficina1").Value,
                "admin@oficina1.com",
                DateTime.UtcNow,
                TimeSpan.FromMinutes(15)
            )
            .Value;
        var reservations = new FakeReservationRepository { ActiveReservation = reservation };

        var result = await CheckSubdomainAvailabilityHandler.Handle(
            new CheckSubdomainAvailabilityQuery("oficina1"),
            new FakeTenantDomainRepository(),
            reservations,
            CancellationToken.None
        );

        Assert.False(result.Value.Available);
        Assert.Equal("TenantDomain.SlugReservedTemporarily", result.Value.Reason);
    }

    [Fact]
    public async Task Well_formed_unclaimed_slug_is_available()
    {
        var result = await CheckSubdomainAvailabilityHandler.Handle(
            new CheckSubdomainAvailabilityQuery("oficina1"),
            new FakeTenantDomainRepository(),
            new FakeReservationRepository(),
            CancellationToken.None
        );

        Assert.True(result.Value.Available);
        Assert.Null(result.Value.Reason);
    }
}
