using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Terms;
using TaxVision.Auth.Application.Terms.Commands;
using TaxVision.Auth.Application.Terms.Queries;
using TaxVision.Auth.Domain.Terms;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase L1.4 — AcceptTermsHandler y GetTermsAcceptanceStatusHandler.</summary>
public sealed class TermsHandlerTests
{
    private sealed class FakeTenantTermsAcceptanceRepository : ITenantTermsAcceptanceRepository
    {
        private readonly List<TenantTermsAcceptance> _all = [];

        public TenantTermsAcceptance? Added { get; private set; }

        public void Seed(TenantTermsAcceptance acceptance) => _all.Add(acceptance);

        public Task AddAsync(TenantTermsAcceptance acceptance, CancellationToken ct = default)
        {
            Added = acceptance;
            _all.Add(acceptance);
            return Task.CompletedTask;
        }

        public Task<TenantTermsAcceptance?> GetLatestAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(
                _all.Where(a => a.TenantId == tenantId).OrderByDescending(a => a.AcceptedAtUtc).FirstOrDefault()
            );
    }

    private static IOptions<TermsOptions> CurrentVersion(string version) =>
        Options.Create(new TermsOptions { CurrentVersion = version });

    [Fact]
    public async Task AcceptTerms_records_an_acceptance_audits_and_publishes_the_event()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var audit = new FakeAuthAuditWriter();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var response = await AcceptTermsHandler.Handle(
            new AcceptTermsCommand(tenantId, userId),
            acceptances,
            CurrentVersion("2026-07-14"),
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
        Assert.Single(audit.Logs, log => log.Action == "tenant.terms_accepted");
        Assert.Single(
            bus.Published.OfType<BuildingBlocks.Messaging.AuthIntegrationEvents.TenantTermsAcceptedIntegrationEvent>()
        );
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task AcceptTerms_called_again_after_a_version_bump_adds_a_new_row_instead_of_mutating_the_old_one()
    {
        var tenantId = Guid.NewGuid();
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var first = TenantTermsAcceptance.Accept(
            tenantId,
            Guid.NewGuid(),
            "2025-01-01",
            null,
            null,
            DateTime.UtcNow.AddDays(-30)
        );
        acceptances.Seed(first);

        await AcceptTermsHandler.Handle(
            new AcceptTermsCommand(tenantId, Guid.NewGuid()),
            acceptances,
            CurrentVersion("2026-07-14"),
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
        var status = await GetTermsAcceptanceStatusHandler.Handle(
            new GetTermsAcceptanceStatusQuery(Guid.NewGuid()),
            new FakeTenantTermsAcceptanceRepository(),
            CurrentVersion("2026-07-14"),
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
        acceptances.Seed(
            TenantTermsAcceptance.Accept(tenantId, Guid.NewGuid(), "2025-01-01", null, null, DateTime.UtcNow)
        );

        var status = await GetTermsAcceptanceStatusHandler.Handle(
            new GetTermsAcceptanceStatusQuery(tenantId),
            acceptances,
            CurrentVersion("2026-07-14"),
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
        acceptances.Seed(
            TenantTermsAcceptance.Accept(tenantId, Guid.NewGuid(), "2026-07-14", null, null, DateTime.UtcNow)
        );

        var status = await GetTermsAcceptanceStatusHandler.Handle(
            new GetTermsAcceptanceStatusQuery(tenantId),
            acceptances,
            CurrentVersion("2026-07-14"),
            CancellationToken.None
        );

        Assert.True(status.Accepted);
    }
}
