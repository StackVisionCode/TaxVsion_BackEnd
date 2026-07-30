using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.SubdomainReservations.Commands;
using TaxVision.Auth.Application.Onboarding.SubdomainReservations.Queries;
using TaxVision.Auth.Domain.Onboarding.SubdomainReservations;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 14 — chequeo local (reserva activa) + M2M a Tenant (subdomain-available)
/// para el flujo de reserva de subdominio post-pago.</summary>
public sealed class OnboardingSubdomainHandlersTests
{
    private static readonly IOptions<OnboardingOptions> Options = Microsoft.Extensions.Options.Options.Create(
        new OnboardingOptions { SubdomainReservationTtlMinutes = 60 }
    );

    [Fact]
    public async Task Check_reports_available_when_free_locally_and_in_tenant()
    {
        var reservations = new FakeOnboardingSubdomainReservationRepository();
        var tenantAvailability = new FakeTenantSubdomainAvailabilityClient { Taken = false };

        var result = await CheckSubdomainAvailabilityHandler.Handle(
            new CheckSubdomainAvailabilityQuery("adas-office", Guid.NewGuid()),
            reservations,
            tenantAvailability,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Available);
        Assert.Null(result.Value.Reason);
    }

    [Fact]
    public async Task Check_reports_unavailable_for_an_invalid_slug()
    {
        var reservations = new FakeOnboardingSubdomainReservationRepository();
        var tenantAvailability = new FakeTenantSubdomainAvailabilityClient { Taken = false };

        var result = await CheckSubdomainAvailabilityHandler.Handle(
            new CheckSubdomainAvailabilityQuery("admin", Guid.NewGuid()),
            reservations,
            tenantAvailability,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Available);
        Assert.Equal("TenantDomain.SlugReserved", result.Value.Reason);
    }

    [Fact]
    public async Task Check_reports_unavailable_when_reserved_by_another_onboarding()
    {
        var now = DateTime.UtcNow;
        var reservations = new FakeOnboardingSubdomainReservationRepository();
        var otherOnboardingId = Guid.NewGuid();
        reservations.Seed(
            OnboardingSubdomainReservation
                .Create(
                    SubdomainSlug.Create("adas-office").Value,
                    otherOnboardingId,
                    "other@example.com",
                    now,
                    TimeSpan.FromMinutes(60)
                )
                .Value
        );
        var tenantAvailability = new FakeTenantSubdomainAvailabilityClient { Taken = false };

        var result = await CheckSubdomainAvailabilityHandler.Handle(
            new CheckSubdomainAvailabilityQuery("adas-office", Guid.NewGuid()),
            reservations,
            tenantAvailability,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Available);
        Assert.Equal("Onboarding.SubdomainReservedTemporarily", result.Value.Reason);
    }

    [Fact]
    public async Task Check_reports_unavailable_when_already_taken_in_tenant()
    {
        var reservations = new FakeOnboardingSubdomainReservationRepository();
        var tenantAvailability = new FakeTenantSubdomainAvailabilityClient { Taken = true };

        var result = await CheckSubdomainAvailabilityHandler.Handle(
            new CheckSubdomainAvailabilityQuery("adas-office", Guid.NewGuid()),
            reservations,
            tenantAvailability,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Available);
        Assert.Equal("Onboarding.SubdomainTaken", result.Value.Reason);
    }

    [Fact]
    public async Task Check_propagates_failure_when_the_tenant_m2m_call_fails()
    {
        var reservations = new FakeOnboardingSubdomainReservationRepository();
        var tenantAvailability = new FakeTenantSubdomainAvailabilityClient
        {
            Failure = new Error("Tenant.Unreachable", "boom"),
        };

        var result = await CheckSubdomainAvailabilityHandler.Handle(
            new CheckSubdomainAvailabilityQuery("adas-office", Guid.NewGuid()),
            reservations,
            tenantAvailability,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Unreachable", result.Error.Code);
    }

    [Fact]
    public async Task Reserve_creates_a_new_active_reservation_for_a_free_slug()
    {
        var reservations = new FakeOnboardingSubdomainReservationRepository();
        var tenantAvailability = new FakeTenantSubdomainAvailabilityClient { Taken = false };
        var unitOfWork = new FakeUnitOfWork();
        var onboardingId = Guid.NewGuid();

        var result = await ReserveSubdomainForOnboardingHandler.Handle(
            new ReserveSubdomainForOnboardingCommand("adas-office", onboardingId, "ada@example.com"),
            reservations,
            new FakeTenantSubdomainReservationRepository(),
            tenantAvailability,
            Options,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Available);
        Assert.NotNull(result.Value.ExpiresAtUtc);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var stored = await reservations.GetActiveBySlugAsync("adas-office", DateTime.UtcNow, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(onboardingId, stored!.OnboardingId);
    }

    [Fact]
    public async Task Reserve_renews_an_existing_active_reservation_for_the_same_onboarding()
    {
        var now = DateTime.UtcNow;
        var onboardingId = Guid.NewGuid();
        var reservations = new FakeOnboardingSubdomainReservationRepository();
        var existing = OnboardingSubdomainReservation
            .Create(
                SubdomainSlug.Create("adas-office").Value,
                onboardingId,
                "ada@example.com",
                now,
                TimeSpan.FromMinutes(1)
            )
            .Value;
        reservations.Seed(existing);
        var tenantAvailability = new FakeTenantSubdomainAvailabilityClient { Taken = false };
        var unitOfWork = new FakeUnitOfWork();

        var result = await ReserveSubdomainForOnboardingHandler.Handle(
            new ReserveSubdomainForOnboardingCommand("adas-office", onboardingId, "ada@example.com"),
            reservations,
            new FakeTenantSubdomainReservationRepository(),
            tenantAvailability,
            Options,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Available);
        Assert.True(result.Value.ExpiresAtUtc > now.AddMinutes(1));
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Reserve_fails_when_reserved_by_another_onboarding()
    {
        var now = DateTime.UtcNow;
        var reservations = new FakeOnboardingSubdomainReservationRepository();
        reservations.Seed(
            OnboardingSubdomainReservation
                .Create(
                    SubdomainSlug.Create("adas-office").Value,
                    Guid.NewGuid(),
                    "other@example.com",
                    now,
                    TimeSpan.FromMinutes(60)
                )
                .Value
        );
        var tenantAvailability = new FakeTenantSubdomainAvailabilityClient { Taken = false };
        var unitOfWork = new FakeUnitOfWork();

        var result = await ReserveSubdomainForOnboardingHandler.Handle(
            new ReserveSubdomainForOnboardingCommand("adas-office", Guid.NewGuid(), "buyer@example.com"),
            reservations,
            new FakeTenantSubdomainReservationRepository(),
            tenantAvailability,
            Options,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Available);
        Assert.Equal("Onboarding.SubdomainReservedTemporarily", result.Value.Reason);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    // Auditoría F11 — regresión real del cross-check: un slug reservado por Path A (alta directa)
    // debe bloquear también a Path C (Onboarding, pago-primero), no solo su propia tabla.
    [Fact]
    public async Task Reserve_fails_when_reserved_by_tenant_domains_path()
    {
        var tenantDomainReservation = TenantSubdomainReservation
            .Create(
                SubdomainSlug.Create("adas-office").Value,
                "other@example.com",
                DateTime.UtcNow,
                TimeSpan.FromMinutes(15)
            )
            .Value;
        var reservations = new FakeOnboardingSubdomainReservationRepository();
        var tenantDomainReservations = new FakeTenantSubdomainReservationRepository
        {
            ActiveReservation = tenantDomainReservation,
        };
        var tenantAvailability = new FakeTenantSubdomainAvailabilityClient { Taken = false };
        var unitOfWork = new FakeUnitOfWork();

        var result = await ReserveSubdomainForOnboardingHandler.Handle(
            new ReserveSubdomainForOnboardingCommand("adas-office", Guid.NewGuid(), "buyer@example.com"),
            reservations,
            tenantDomainReservations,
            tenantAvailability,
            Options,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Available);
        Assert.Equal("Onboarding.SubdomainReservedTemporarily", result.Value.Reason);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Reserve_fails_when_already_taken_in_tenant()
    {
        var reservations = new FakeOnboardingSubdomainReservationRepository();
        var tenantAvailability = new FakeTenantSubdomainAvailabilityClient { Taken = true };
        var unitOfWork = new FakeUnitOfWork();

        var result = await ReserveSubdomainForOnboardingHandler.Handle(
            new ReserveSubdomainForOnboardingCommand("adas-office", Guid.NewGuid(), "buyer@example.com"),
            reservations,
            new FakeTenantSubdomainReservationRepository(),
            tenantAvailability,
            Options,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Available);
        Assert.Equal("Onboarding.SubdomainTaken", result.Value.Reason);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private sealed class FakeTenantSubdomainAvailabilityClient : ITenantSubdomainAvailabilityClient
    {
        public bool Taken { get; set; }
        public Error? Failure { get; set; }

        public Task<Result<bool>> IsTakenAsync(string slug, CancellationToken ct = default) =>
            Task.FromResult(Failure is not null ? Result.Failure<bool>(Failure) : Result.Success(Taken));
    }

    // Auditoría F11 — cross-check contra Path A (TenantDomains); vacío por defecto (sin reserva activa).
    private sealed class FakeTenantSubdomainReservationRepository : ITenantSubdomainReservationRepository
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

    private sealed class FakeOnboardingSubdomainReservationRepository : IOnboardingSubdomainReservationRepository
    {
        private readonly List<OnboardingSubdomainReservation> _reservations = [];

        public void Seed(OnboardingSubdomainReservation reservation) => _reservations.Add(reservation);

        public Task<OnboardingSubdomainReservation?> GetActiveBySlugAsync(
            string slug,
            DateTime nowUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _reservations.FirstOrDefault(r => r.Slug == slug && r.ConsumedAtUtc is null && r.ExpiresAtUtc > nowUtc)
            );

        public Task AddAsync(OnboardingSubdomainReservation reservation, CancellationToken ct = default)
        {
            _reservations.Add(reservation);
            return Task.CompletedTask;
        }
    }
}
