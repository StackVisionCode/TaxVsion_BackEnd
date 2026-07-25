using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Application.TenantDomains.Commands;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase A7 — ReserveSubdomand bloquea un slug para un email mientras el registro se completa.</summary>
public sealed class ReserveSubdomainHandlerTests
{
    private sealed class FakeJwtTokenGenerator : IJwtTokenGenerator
    {
        public string? LastTicketSlug { get; private set; }
        public string? LastTicketEmail { get; private set; }

        public AccessToken Generate(
            User user,
            string effectiveTimeZoneId,
            Guid sessionId,
            IReadOnlyCollection<string> roles,
            IReadOnlyCollection<string> authMethods
        ) => new("fake-access-token", 900);

        public AccessToken GenerateServiceToken(
            Guid tenantId,
            string clientId,
            IReadOnlyCollection<string> permissions,
            int lifetimeMinutes
        ) => new("fake-service-token", lifetimeMinutes * 60);

        public AccessToken GenerateTenantRegistrationTicket(string slug, string email, DateTime expiresAtUtc)
        {
            LastTicketSlug = slug;
            LastTicketEmail = email;
            return new AccessToken("fake-registration-ticket", 900);
        }
    }

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
        public TenantSubdomainReservation? Added { get; private set; }

        public Task<TenantSubdomainReservation?> GetActiveBySlugAsync(
            string slug,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Task.FromResult(ActiveReservation);

        public Task AddAsync(TenantSubdomainReservation reservation, CancellationToken ct = default)
        {
            Added = reservation;
            return Task.CompletedTask;
        }
    }

    private static IOptions<TenantDomainOptions> DefaultOptions() =>
        Microsoft.Extensions.Options.Options.Create(new TenantDomainOptions { SubdomainReservationTtlMinutes = 15 });

    [Fact]
    public async Task Malformed_slug_fails_without_touching_repositories()
    {
        var reservations = new FakeReservationRepository();

        var result = await ReserveSubdomainHandler.Handle(
            new ReserveSubdomainCommand("ab", "admin@oficina1.com"),
            new FakeTenantDomainRepository(),
            reservations,
            DefaultOptions(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeJwtTokenGenerator(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugLength", result.Error.Code);
        Assert.Null(reservations.Added);
    }

    [Fact]
    public async Task Slug_already_claimed_by_a_tenant_domain_fails()
    {
        var domains = new FakeTenantDomainRepository { SlugTaken = true };
        var reservations = new FakeReservationRepository();

        var result = await ReserveSubdomainHandler.Handle(
            new ReserveSubdomainCommand("oficina1", "admin@oficina1.com"),
            domains,
            reservations,
            DefaultOptions(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeJwtTokenGenerator(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugTaken", result.Error.Code);
        Assert.Null(reservations.Added);
    }

    [Fact]
    public async Task Slug_with_an_active_reservation_fails()
    {
        var existing = TenantSubdomainReservation
            .Create(
                SubdomainSlug.Create("oficina1").Value,
                "otro@oficina1.com",
                DateTime.UtcNow,
                TimeSpan.FromMinutes(15)
            )
            .Value;
        var reservations = new FakeReservationRepository { ActiveReservation = existing };

        var result = await ReserveSubdomainHandler.Handle(
            new ReserveSubdomainCommand("oficina1", "admin@oficina1.com"),
            new FakeTenantDomainRepository(),
            reservations,
            DefaultOptions(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeJwtTokenGenerator(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugReservedTemporarily", result.Error.Code);
        Assert.Null(reservations.Added);
    }

    [Fact]
    public async Task Well_formed_unclaimed_slug_is_reserved()
    {
        var reservations = new FakeReservationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var jwt = new FakeJwtTokenGenerator();

        var result = await ReserveSubdomainHandler.Handle(
            new ReserveSubdomainCommand("Oficina1", "Admin@Oficina1.com"),
            new FakeTenantDomainRepository(),
            reservations,
            DefaultOptions(),
            unitOfWork,
            bus,
            jwt,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("oficina1", result.Value.Slug);
        Assert.Equal("admin@oficina1.com", result.Value.ReservedByEmail);
        Assert.Equal("fake-registration-ticket", result.Value.RegistrationTicket);
        Assert.Equal("oficina1", jwt.LastTicketSlug);
        Assert.Equal("admin@oficina1.com", jwt.LastTicketEmail);
        Assert.NotNull(reservations.Added);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Single(
            bus.Published.OfType<TenantDomainReservedIntegrationEvent>(),
            evt => evt.Slug == "oficina1" && evt.ReservedByEmail == "admin@oficina1.com"
        );
    }
}
