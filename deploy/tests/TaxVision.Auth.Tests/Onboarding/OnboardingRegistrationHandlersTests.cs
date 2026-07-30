using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.Registration.Commands;
using TaxVision.Auth.Application.Onboarding.Registration.Queries;
using TaxVision.Auth.Domain.Onboarding.SubdomainReservations;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 13 — preview/complete/status del form público de registro.</summary>
public sealed class OnboardingRegistrationHandlersTests
{
    private static readonly SecureTokenService Tokens = new();

    private static (TenantOnboarding onboarding, string rawToken) NewPendingRegistration(DateTime now)
    {
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        Assert.True(onboarding.MarkPaymentProcessing(Guid.NewGuid(), "pi_123").IsSuccess);
        Assert.True(onboarding.MarkPaymentCompleted("pi_123", now).IsSuccess);

        var rawToken = Tokens.GenerateToken();
        var hash = RegistrationTokenHash.Create(Tokens.Hash(rawToken)).Value;
        Assert.True(onboarding.SetRegistrationToken(hash, now.AddHours(72)).IsSuccess);

        return (onboarding, rawToken);
    }

    [Fact]
    public async Task Preview_returns_the_buyer_identity_for_a_valid_token()
    {
        var now = DateTime.UtcNow;
        var (onboarding, rawToken) = NewPendingRegistration(now);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };

        var result = await PreviewRegistrationHandler.Handle(
            new PreviewRegistrationQuery(rawToken),
            onboardings,
            Tokens,
            new FakePlanCatalogClient(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(onboarding.FirstName, result.Value.FirstName);
        Assert.Equal(onboarding.LastName, result.Value.LastName);
        Assert.StartsWith("bu***@", result.Value.MaskedEmail);
        Assert.EndsWith("@example.com", result.Value.MaskedEmail);
    }

    [Fact]
    public async Task Preview_fails_for_an_unknown_token()
    {
        var onboardings = new FakeTenantOnboardingRepository();

        var result = await PreviewRegistrationHandler.Handle(
            new PreviewRegistrationQuery("not-a-real-token"),
            onboardings,
            Tokens,
            new FakePlanCatalogClient(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidToken", result.Error.Code);
    }

    [Fact]
    public async Task Preview_fails_for_an_already_used_token()
    {
        var now = DateTime.UtcNow;
        var (onboarding, rawToken) = NewPendingRegistration(now);
        Assert.True(onboarding.ConsumeRegistrationToken(now).IsSuccess);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };

        var result = await PreviewRegistrationHandler.Handle(
            new PreviewRegistrationQuery(rawToken),
            onboardings,
            Tokens,
            new FakePlanCatalogClient(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TokenUsed", result.Error.Code);
    }

    [Fact]
    public async Task Complete_starts_provisioning_and_publishes_the_event_with_a_platform_tenant_id()
    {
        var now = DateTime.UtcNow;
        var (onboarding, rawToken) = NewPendingRegistration(now);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var termsVersion = TermsVersion
            .Publish(
                TermsKind.TermsOfService,
                "v1",
                "https://example.com/tos",
                new string('a', 64),
                "en",
                Guid.NewGuid(),
                now.AddDays(-1)
            )
            .Value;
        var termsVersions = new FakeTermsVersionRepository { ById = termsVersion };
        var subdomainReservations = new FakeOnboardingSubdomainReservationRepository();
        subdomainReservations.Seed(
            OnboardingSubdomainReservation
                .Create(
                    SubdomainSlug.Create("adas-office").Value,
                    onboarding.Id,
                    onboarding.Email,
                    now,
                    TimeSpan.FromMinutes(60)
                )
                .Value
        );
        var passwordHasher = new FakePasswordHasher();
        var passwordHashReferences = new FakeTokenReferenceStore();
        var requestContext = new FakeRequestContext();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();

        var result = await CompleteOnboardingRegistrationHandler.Handle(
            new CompleteOnboardingRegistrationCommand(
                rawToken,
                "a-very-strong-password-123",
                "Ada's Tax Office",
                "adas-office",
                true,
                termsVersion.Id
            ),
            onboardings,
            termsVersions,
            subdomainReservations,
            Tokens,
            passwordHasher,
            passwordHashReferences,
            requestContext,
            unitOfWork,
            bus,
            correlation,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Provisioning", result.Value.Status);
        Assert.Equal(TenantOnboardingStatus.Provisioning, onboarding.Status);
        Assert.NotNull(onboarding.RegistrationTokenUsedAtUtc);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var published = Assert.Single(bus.Published);
        var started =
            Assert.IsType<BuildingBlocks.Messaging.AuthIntegrationEvents.OnboardingProvisioningStartedIntegrationEvent>(
                published
            );
        Assert.Equal(PlatformTenant.Id, started.TenantId);
        Assert.Equal(onboarding.Id, started.OnboardingId);
        Assert.Equal("adas-office", started.RequestedSubdomain);
        Assert.Equal(passwordHashReferences.Reference, started.PasswordHashReference);
        Assert.Equal(passwordHasher.LastHash, passwordHashReferences.Stored);
    }

    [Fact]
    public async Task Complete_fails_when_terms_are_not_accepted()
    {
        var now = DateTime.UtcNow;
        var (onboarding, rawToken) = NewPendingRegistration(now);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };

        var result = await CompleteOnboardingRegistrationHandler.Handle(
            new CompleteOnboardingRegistrationCommand(
                rawToken,
                "a-very-strong-password-123",
                "Ada's Tax Office",
                "adas-office",
                false,
                Guid.NewGuid()
            ),
            onboardings,
            new FakeTermsVersionRepository(),
            new FakeOnboardingSubdomainReservationRepository(),
            Tokens,
            new FakePasswordHasher(),
            new FakeTokenReferenceStore(),
            new FakeRequestContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsNotAccepted", result.Error.Code);
        Assert.Equal(TenantOnboardingStatus.RegistrationPending, onboarding.Status);
    }

    [Fact]
    public async Task Complete_fails_for_a_reserved_subdomain()
    {
        var now = DateTime.UtcNow;
        var (onboarding, rawToken) = NewPendingRegistration(now);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var termsVersion = TermsVersion
            .Publish(
                TermsKind.TermsOfService,
                "v1",
                "https://example.com/tos",
                new string('a', 64),
                "en",
                Guid.NewGuid(),
                now.AddDays(-1)
            )
            .Value;

        var result = await CompleteOnboardingRegistrationHandler.Handle(
            new CompleteOnboardingRegistrationCommand(
                rawToken,
                "a-very-strong-password-123",
                "Ada's Tax Office",
                "admin",
                true,
                termsVersion.Id
            ),
            onboardings,
            new FakeTermsVersionRepository { ById = termsVersion },
            new FakeOnboardingSubdomainReservationRepository(),
            Tokens,
            new FakePasswordHasher(),
            new FakeTokenReferenceStore(),
            new FakeRequestContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugReserved", result.Error.Code);
    }

    [Fact]
    public async Task Complete_fails_when_the_terms_version_is_no_longer_current()
    {
        var now = DateTime.UtcNow;
        var (onboarding, rawToken) = NewPendingRegistration(now);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var expiredTermsVersion = TermsVersion
            .Publish(
                TermsKind.TermsOfService,
                "v0",
                "https://example.com/tos-old",
                new string('a', 64),
                "en",
                Guid.NewGuid(),
                now.AddDays(-10),
                now.AddDays(-1)
            )
            .Value;

        var result = await CompleteOnboardingRegistrationHandler.Handle(
            new CompleteOnboardingRegistrationCommand(
                rawToken,
                "a-very-strong-password-123",
                "Ada's Tax Office",
                "adas-office",
                true,
                expiredTermsVersion.Id
            ),
            onboardings,
            new FakeTermsVersionRepository { ById = expiredTermsVersion },
            new FakeOnboardingSubdomainReservationRepository(),
            Tokens,
            new FakePasswordHasher(),
            new FakeTokenReferenceStore(),
            new FakeRequestContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsVersionNotCurrent", result.Error.Code);
    }

    [Fact]
    public async Task Status_exposes_a_redirect_url_once_completed()
    {
        var now = DateTime.UtcNow;
        var (onboarding, rawToken) = NewPendingRegistration(now);
        var termsVersion = TermsVersion
            .Publish(
                TermsKind.TermsOfService,
                "v1",
                "https://example.com/tos",
                new string('a', 64),
                "en",
                Guid.NewGuid(),
                now.AddDays(-1)
            )
            .Value;
        Assert.True(
            onboarding
                .StartProvisioning(
                    "Ada's Tax Office",
                    "adas-office",
                    termsVersion.Id,
                    termsVersion.ContentHash!,
                    "127.0.0.1",
                    "xunit",
                    now
                )
                .IsSuccess
        );
        for (var step = TenantProvisioningStep.Tenant; step != TenantProvisioningStep.Completed; )
        {
            Assert.True(onboarding.MarkStepCompleted(step).IsSuccess);
            step = onboarding.CurrentStep;
        }
        Assert.True(onboarding.MarkCompleted(now).IsSuccess);

        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var options = Options.Create(new OnboardingOptions { TenantBaseDomain = "taxprocore.com" });

        var result = await GetOnboardingStatusHandler.Handle(
            new GetOnboardingStatusQuery(rawToken),
            onboardings,
            Tokens,
            options,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value.Status);
        Assert.Equal("https://adas-office.taxprocore.com", result.Value.RedirectUrl);
    }

    [Fact]
    public async Task Status_fails_for_an_unknown_token()
    {
        var onboardings = new FakeTenantOnboardingRepository();
        var options = Options.Create(new OnboardingOptions());

        var result = await GetOnboardingStatusHandler.Handle(
            new GetOnboardingStatusQuery("not-a-real-token"),
            onboardings,
            Tokens,
            options,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidToken", result.Error.Code);
    }

    private sealed class FakeTermsVersionRepository : ITermsVersionRepository
    {
        public TermsVersion? ById { get; set; }

        public Task AddAsync(TermsVersion version, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TermsVersion?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(ById?.Id == id ? ById : null);

        public Task<TermsVersion?> GetCurrentAsync(
            TermsKind kind,
            string locale,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string? LastHash { get; private set; }

        public string Hash(string password)
        {
            LastHash = $"hashed:{password}";
            return LastHash;
        }

        public bool Verify(string password, string hash) => hash == $"hashed:{password}";
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
