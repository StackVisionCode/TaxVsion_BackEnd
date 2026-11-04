using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Tests.Application;

/// <summary>
/// El plan llega por entitlements (<c>signature.*</c>) a una proyección local; el resolvedor la combina con
/// la excepción de PlatformAdmin y con lo que eligió la oficina.
/// </summary>
public sealed class SignatureLimitsResolverTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task The_plan_keys_are_projected_and_missing_ones_keep_the_previous_values()
    {
        var limits = new InMemoryLimits();

        await Project(
            limits,
            1,
            new Dictionary<string, string>
            {
                ["signature.max_documents_per_request"] = "40",
                ["signature.channels"] = "Email,Sms,WhatsApp",
                ["signature.purge_allowed"] = "True",
            }
        );

        var plan = (await limits.GetByTenantIdAsync(Tenant))!.ToLimits();
        Assert.Equal(40, plan.MaxDocumentsPerRequest);
        Assert.Equal(TenantSignatureLimits.FallbackMaxSignersPerRequest, plan.MaxSignersPerRequest);
        Assert.Equal(
            VerificationChannel.Email | VerificationChannel.Sms | VerificationChannel.WhatsApp,
            plan.AllowedChannels
        );
        Assert.True(plan.PurgeAllowed);
    }

    [Fact]
    public async Task An_older_revision_arriving_late_does_not_overwrite_a_newer_one()
    {
        var limits = new InMemoryLimits();

        await Project(limits, 5, new Dictionary<string, string> { ["signature.max_signers_per_request"] = "80" });
        await Project(limits, 4, new Dictionary<string, string> { ["signature.max_signers_per_request"] = "10" });

        Assert.Equal(80, (await limits.GetByTenantIdAsync(Tenant))!.MaxSignersPerRequest);
    }

    [Fact]
    public async Task Without_plan_event_or_office_settings_the_previous_values_rule()
    {
        var resolver = new SignatureLimitsResolver(new InMemoryLimits(), new InMemorySettings(null));

        var policy = await resolver.ResolveAsync(Tenant);

        Assert.Equal(TenantSignatureLimits.FallbackMaxDocumentsPerRequest, policy.ForRequests.MaxDocumentsPerRequest);
        Assert.Equal(TenantSignatureSettings.DefaultTokenExpirationHours, policy.DefaultTokenHours);
        Assert.Equal(VerificationChannel.Email, policy.DefaultChannel);
    }

    [Fact]
    public async Task The_platform_exception_sits_on_top_of_the_plan()
    {
        var limits = new InMemoryLimits();
        await Project(limits, 1, new Dictionary<string, string> { ["signature.max_documents_per_request"] = "20" });
        var settings = TenantSignatureSettings.CreateForNewTenant(Tenant, "secret").Value;
        settings.ApplyPlanConstraints(
            SignaturePlanConstraints.Create(60, null, null, null, null, null, null, null).Value
        );
        var resolver = new SignatureLimitsResolver(limits, new InMemorySettings(settings));

        var policy = await resolver.ResolveAsync(Tenant);

        Assert.Equal(60, policy.Ceilings.MaxDocumentsPerRequest);
        Assert.Equal(60, policy.ForRequests.MaxDocumentsPerRequest);
    }

    private static Task Project(InMemoryLimits limits, long revision, IReadOnlyDictionary<string, string> values) =>
        TenantEntitlementsChangedSignatureLimitsConsumer.Handle(
            new TenantEntitlementsChangedIntegrationEvent
            {
                TenantId = Tenant,
                RevisionNumber = revision,
                ChangedKeys = [],
                PlanCode = "pro",
                SubscriptionStatus = "Active",
                SeatCount = 1,
                AvailableSeatCount = 1,
                EntitlementValues = values,
            },
            limits,
            new NoOpUnitOfWork(),
            new NoCorrelation(),
            CancellationToken.None
        );

    private sealed class InMemoryLimits : ITenantSignatureLimitsRepository
    {
        private readonly Dictionary<Guid, TenantSignatureLimits> _byTenant = [];

        public Task<TenantSignatureLimits?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_byTenant.GetValueOrDefault(tenantId));

        public void Add(TenantSignatureLimits limits) => _byTenant[limits.TenantId] = limits;
    }

    private sealed class InMemorySettings(TenantSignatureSettings? settings) : ITenantSignatureSettingsRepository
    {
        public Task<TenantSignatureSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(settings);

        public Task AddAsync(TenantSignatureSettings toAdd, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Guid>> ListTenantIdsWithPurgeEnabledAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class NoCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
