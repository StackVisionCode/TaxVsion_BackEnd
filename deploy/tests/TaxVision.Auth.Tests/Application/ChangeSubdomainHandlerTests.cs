using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Application.TenantDomains.Commands;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Fase A7 — ChangeSubdomain renombra el subdominio primario ya activo. La auditoría y
/// el integration event ya no se prueban acá: los produce TenantSubdomainChangedHandler
/// cuando AuthDbContext.SaveChangesAsync los despacha (ver TenantSubdomainChangedHandlerTests).
/// </summary>
public sealed class ChangeSubdomainHandlerTests
{
    private sealed class FakeTenantDomainRepository : ITenantDomainRepository
    {
        private readonly Dictionary<Guid, TenantDomain> _byId = [];

        public void Seed(TenantDomain domain) => _byId[domain.Id] = domain;

        public bool SlugTaken { get; set; }

        public Task<TenantDomain?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_byId.GetValueOrDefault(id));

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

    private static IOptions<TenantDomainOptions> DefaultOptions() =>
        Microsoft.Extensions.Options.Options.Create(new TenantDomainOptions { BaseDomain = "taxprocore.com" });

    private static TenantDomain PrimarySubdomain(Guid tenantId) =>
        TenantDomain
            .CreateSubdomain(
                tenantId,
                SubdomainSlug.Create("oficina1").Value,
                "taxprocore.com",
                Guid.NewGuid(),
                DateTime.UtcNow,
                isPrimary: true
            )
            .Value;

    [Fact]
    public async Task Domain_belonging_to_another_tenant_is_not_found()
    {
        var domain = PrimarySubdomain(Guid.NewGuid());
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);

        var result = await ChangeSubdomainHandler.Handle(
            new ChangeSubdomainCommand(Guid.NewGuid(), domain.Id, "oficina2", Guid.NewGuid()),
            domains,
            new FakeReservationRepository(),
            DefaultOptions(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Malformed_new_slug_fails()
    {
        var tenantId = Guid.NewGuid();
        var domain = PrimarySubdomain(tenantId);
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);

        var result = await ChangeSubdomainHandler.Handle(
            new ChangeSubdomainCommand(tenantId, domain.Id, "ab", Guid.NewGuid()),
            domains,
            new FakeReservationRepository(),
            DefaultOptions(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugLength", result.Error.Code);
    }

    [Fact]
    public async Task New_slug_already_claimed_by_another_tenant_domain_fails()
    {
        var tenantId = Guid.NewGuid();
        var domain = PrimarySubdomain(tenantId);
        var domains = new FakeTenantDomainRepository { SlugTaken = true };
        domains.Seed(domain);

        var result = await ChangeSubdomainHandler.Handle(
            new ChangeSubdomainCommand(tenantId, domain.Id, "oficina2", Guid.NewGuid()),
            domains,
            new FakeReservationRepository(),
            DefaultOptions(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugTaken", result.Error.Code);
    }

    [Fact]
    public async Task Requesting_the_domain_s_own_current_slug_does_not_treat_it_as_taken()
    {
        var tenantId = Guid.NewGuid();
        var domain = PrimarySubdomain(tenantId);
        // SlugExistsAsync would return true for ANY slug here — proving the handler
        // special-cases "same as current" instead of trusting the repo blindly.
        var domains = new FakeTenantDomainRepository { SlugTaken = true };
        domains.Seed(domain);

        var result = await ChangeSubdomainHandler.Handle(
            new ChangeSubdomainCommand(tenantId, domain.Id, "oficina1", Guid.NewGuid()),
            domains,
            new FakeReservationRepository(),
            DefaultOptions(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugUnchanged", result.Error.Code);
    }

    [Fact]
    public async Task New_slug_with_an_active_reservation_fails()
    {
        var tenantId = Guid.NewGuid();
        var domain = PrimarySubdomain(tenantId);
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);
        var existing = TenantSubdomainReservation
            .Create(
                SubdomainSlug.Create("oficina2").Value,
                "otro@oficina2.com",
                DateTime.UtcNow,
                TimeSpan.FromMinutes(15)
            )
            .Value;
        var reservations = new FakeReservationRepository { ActiveReservation = existing };

        var result = await ChangeSubdomainHandler.Handle(
            new ChangeSubdomainCommand(tenantId, domain.Id, "oficina2", Guid.NewGuid()),
            domains,
            reservations,
            DefaultOptions(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugReservedTemporarily", result.Error.Code);
    }

    [Fact]
    public async Task Well_formed_unclaimed_new_slug_changes_the_domain_and_raises_the_event()
    {
        var tenantId = Guid.NewGuid();
        var domain = PrimarySubdomain(tenantId);
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);
        var unitOfWork = new FakeUnitOfWork();
        var actingUserId = Guid.NewGuid();

        var result = await ChangeSubdomainHandler.Handle(
            new ChangeSubdomainCommand(tenantId, domain.Id, "Oficina2", actingUserId),
            domains,
            new FakeReservationRepository(),
            DefaultOptions(),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("oficina2.taxprocore.com", domain.Host);
        var evt = Assert.Single(domain.DomainEvents.OfType<TenantSubdomainChanged>());
        Assert.Equal("oficina1.taxprocore.com", evt.OldHost);
        Assert.Equal("oficina2.taxprocore.com", evt.NewHost);
        Assert.Equal(actingUserId, evt.ActingUserId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }
}
