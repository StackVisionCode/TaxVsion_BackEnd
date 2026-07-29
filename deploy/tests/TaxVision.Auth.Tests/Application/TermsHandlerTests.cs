using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Terms.Commands;
using TaxVision.Auth.Application.Terms.Queries;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;
using TaxVision.Auth.Domain.Terms;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase L1.4 / PayFlow Fase 6 — AcceptTermsHandler, AcceptTermsFromOnboardingHandler y GetTermsAcceptanceStatusHandler.</summary>
public sealed class TermsHandlerTests
{
    private static readonly string ValidHash = new('a', 64);

    private sealed class FakeTenantTermsAcceptanceRepository : ITenantTermsAcceptanceRepository
    {
        private readonly List<TenantTermsAcceptance> _all = [];

        public TenantTermsAcceptance? Added { get; private set; }
        public int AddCount { get; private set; }

        public void Seed(TenantTermsAcceptance acceptance) => _all.Add(acceptance);

        public Task AddAsync(TenantTermsAcceptance acceptance, CancellationToken ct = default)
        {
            Added = acceptance;
            AddCount++;
            _all.Add(acceptance);
            return Task.CompletedTask;
        }

        public Task<TenantTermsAcceptance?> GetLatestAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(
                _all.Where(a => a.TenantId == tenantId).OrderByDescending(a => a.AcceptedAtUtc).FirstOrDefault()
            );

        public Task<TenantTermsAcceptance?> GetByVersionAsync(
            Guid tenantId,
            Guid userId,
            Guid termsVersionId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _all.FirstOrDefault(a =>
                    a.TenantId == tenantId && a.AcceptedByUserId == userId && a.TermsVersionId == termsVersionId
                )
            );
    }

    private sealed class FakeTermsVersionRepository : ITermsVersionRepository
    {
        private readonly List<TermsVersion> _all = [];

        public TermsVersion Seed(TermsKind kind, string version, string locale, DateTime effectiveFromUtc)
        {
            var published = TermsVersion
                .Publish(
                    kind,
                    version,
                    "https://taxvision.example.com/legal/" + version,
                    ValidHash,
                    locale,
                    Guid.NewGuid(),
                    effectiveFromUtc
                )
                .Value;
            _all.Add(published);
            return published;
        }

        public Task AddAsync(TermsVersion version, CancellationToken ct = default)
        {
            _all.Add(version);
            return Task.CompletedTask;
        }

        public Task<TermsVersion?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_all.FirstOrDefault(v => v.Id == id));

        public Task<TermsVersion?> GetCurrentAsync(
            TermsKind kind,
            string locale,
            DateTime nowUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _all.Where(v =>
                        v.Kind == kind
                        && v.Locale == locale
                        && v.EffectiveFromUtc <= nowUtc
                        && (v.EffectiveUntilUtc == null || v.EffectiveUntilUtc > nowUtc)
                    )
                    .OrderByDescending(v => v.EffectiveFromUtc)
                    .FirstOrDefault()
            );
    }

    [Fact]
    public async Task AcceptTerms_records_an_acceptance_audits_and_publishes_the_event()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var versions = new FakeTermsVersionRepository();
        var current = versions.Seed(TermsKind.TermsOfService, "2026-07-14", "en-US", DateTime.UtcNow.AddDays(-1));
        var audit = new FakeAuthAuditWriter();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var response = await AcceptTermsHandler.Handle(
            new AcceptTermsCommand(tenantId, userId),
            acceptances,
            versions,
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.Equal("2026-07-14", response.TermsVersion);
        Assert.NotNull(acceptances.Added);
        Assert.Equal(tenantId, acceptances.Added!.TenantId);
        Assert.Equal(userId, acceptances.Added!.AcceptedByUserId);
        Assert.Equal(current.Id, acceptances.Added!.TermsVersionId);
        Assert.Equal("ReAcceptance", acceptances.Added!.AcceptedInContext);
        Assert.Single(audit.Logs, log => log.Action == "tenant.terms_accepted");
        Assert.Single(
            bus.Published.OfType<BuildingBlocks.Messaging.AuthIntegrationEvents.TenantTermsAcceptedIntegrationEvent>()
        );
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task AcceptTerms_called_twice_for_the_same_version_is_idempotent()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var versions = new FakeTermsVersionRepository();
        versions.Seed(TermsKind.TermsOfService, "2026-07-14", "en-US", DateTime.UtcNow.AddDays(-1));

        var command = new AcceptTermsCommand(tenantId, userId);
        var first = await AcceptTermsHandler.Handle(
            command,
            acceptances,
            versions,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );
        var second = await AcceptTermsHandler.Handle(
            command,
            acceptances,
            versions,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Equal(1, acceptances.AddCount);
        Assert.Equal(first.AcceptedAtUtc, second.AcceptedAtUtc);
    }

    [Fact]
    public async Task AcceptTerms_after_a_version_bump_adds_a_new_row_instead_of_mutating_the_old_one()
    {
        var tenantId = Guid.NewGuid();
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var versions = new FakeTermsVersionRepository();
        var old = versions.Seed(TermsKind.TermsOfService, "2025-01-01", "en-US", DateTime.UtcNow.AddDays(-60));
        var first = TenantTermsAcceptance.Accept(
            tenantId,
            Guid.NewGuid(),
            "2025-01-01",
            old.Id,
            null,
            "LegacyPreV2",
            null,
            null,
            DateTime.UtcNow.AddDays(-30)
        );
        acceptances.Seed(first);
        versions.Seed(TermsKind.TermsOfService, "2026-07-14", "en-US", DateTime.UtcNow.AddDays(-1));

        await AcceptTermsHandler.Handle(
            new AcceptTermsCommand(tenantId, Guid.NewGuid()),
            acceptances,
            versions,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        var latest = await acceptances.GetLatestAsync(tenantId, CancellationToken.None);
        Assert.Equal("2026-07-14", latest!.TermsVersion);
        Assert.NotEqual(first.Id, latest.Id); // el historial anterior sigue intacto, no se piso
    }

    [Fact]
    public async Task Status_reports_not_accepted_when_the_tenant_never_accepted_anything()
    {
        var versions = new FakeTermsVersionRepository();
        versions.Seed(TermsKind.TermsOfService, "2026-07-14", "en-US", DateTime.UtcNow.AddDays(-1));

        var status = await GetTermsAcceptanceStatusHandler.Handle(
            new GetTermsAcceptanceStatusQuery(Guid.NewGuid()),
            new FakeTenantTermsAcceptanceRepository(),
            versions,
            CancellationToken.None
        );

        Assert.False(status.Accepted);
        Assert.Null(status.AcceptedVersion);
        Assert.Equal("2026-07-14", status.CurrentVersion);
    }

    [Fact]
    public async Task Status_reports_not_accepted_when_the_latest_acceptance_is_for_an_older_version()
    {
        var tenantId = Guid.NewGuid();
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var versions = new FakeTermsVersionRepository();
        var old = versions.Seed(TermsKind.TermsOfService, "2025-01-01", "en-US", DateTime.UtcNow.AddDays(-60));
        versions.Seed(TermsKind.TermsOfService, "2026-07-14", "en-US", DateTime.UtcNow.AddDays(-1));
        acceptances.Seed(
            TenantTermsAcceptance.Accept(
                tenantId,
                Guid.NewGuid(),
                "2025-01-01",
                old.Id,
                null,
                "LegacyPreV2",
                null,
                null,
                DateTime.UtcNow
            )
        );

        var status = await GetTermsAcceptanceStatusHandler.Handle(
            new GetTermsAcceptanceStatusQuery(tenantId),
            acceptances,
            versions,
            CancellationToken.None
        );

        Assert.False(status.Accepted);
        Assert.Equal("2025-01-01", status.AcceptedVersion);
    }

    [Fact]
    public async Task Status_reports_accepted_when_the_latest_acceptance_matches_the_current_version()
    {
        var tenantId = Guid.NewGuid();
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var versions = new FakeTermsVersionRepository();
        var current = versions.Seed(TermsKind.TermsOfService, "2026-07-14", "en-US", DateTime.UtcNow.AddDays(-1));
        acceptances.Seed(
            TenantTermsAcceptance.Accept(
                tenantId,
                Guid.NewGuid(),
                "2026-07-14",
                current.Id,
                null,
                "ReAcceptance",
                null,
                null,
                DateTime.UtcNow
            )
        );

        var status = await GetTermsAcceptanceStatusHandler.Handle(
            new GetTermsAcceptanceStatusQuery(tenantId),
            acceptances,
            versions,
            CancellationToken.None
        );

        Assert.True(status.Accepted);
    }

    [Fact]
    public async Task AcceptTermsFromOnboarding_fails_when_the_terms_version_does_not_exist()
    {
        var result = await AcceptTermsFromOnboardingHandler.Handle(
            new AcceptTermsFromOnboardingCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ValidHash, null, null),
            new FakeTenantTermsAcceptanceRepository(),
            new FakeTermsVersionRepository(),
            new FakeAuthAuditWriter(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TermsVersion.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task AcceptTermsFromOnboarding_records_the_acceptance_with_the_onboarding_context_and_hash()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var versions = new FakeTermsVersionRepository();
        var version = versions.Seed(TermsKind.TermsOfService, "2026-08-01", "en-US", DateTime.UtcNow.AddDays(-1));

        var result = await AcceptTermsFromOnboardingHandler.Handle(
            new AcceptTermsFromOnboardingCommand(
                tenantId,
                userId,
                version.Id,
                ValidHash,
                "203.0.113.9",
                "onboarding-ua"
            ),
            acceptances,
            versions,
            new FakeAuthAuditWriter(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotNull(acceptances.Added);
        Assert.Equal("Onboarding", acceptances.Added!.AcceptedInContext);
        Assert.Equal(ValidHash, acceptances.Added!.ContentHash);
        Assert.Equal(version.Id, acceptances.Added!.TermsVersionId);
    }

    [Fact]
    public async Task AcceptTermsFromOnboarding_called_twice_is_idempotent()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var versions = new FakeTermsVersionRepository();
        var version = versions.Seed(TermsKind.TermsOfService, "2026-08-01", "en-US", DateTime.UtcNow.AddDays(-1));
        var command = new AcceptTermsFromOnboardingCommand(tenantId, userId, version.Id, ValidHash, null, null);

        await AcceptTermsFromOnboardingHandler.Handle(
            command,
            acceptances,
            versions,
            new FakeAuthAuditWriter(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );
        await AcceptTermsFromOnboardingHandler.Handle(
            command,
            acceptances,
            versions,
            new FakeAuthAuditWriter(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Equal(1, acceptances.AddCount);
    }
}
