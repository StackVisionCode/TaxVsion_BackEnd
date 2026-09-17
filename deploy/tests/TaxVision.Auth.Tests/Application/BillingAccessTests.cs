using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Subscriptions.Abstractions;
using TaxVision.Auth.Application.Subscriptions.IntegrationEvents;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

public sealed class BillingAccessTests
{
    [Fact]
    public void Blocked_tenant_blocks_employees_and_clients_but_not_admins()
    {
        var tenant = BuildTenant(blocked: true);

        Assert.True(BillingAccessPolicy.IsBlockedForBilling(tenant, UserActorType.TenantEmployee));
        Assert.True(BillingAccessPolicy.IsBlockedForBilling(tenant, UserActorType.CustomerPortal));
        Assert.False(BillingAccessPolicy.IsBlockedForBilling(tenant, UserActorType.TenantAdmin));
        Assert.False(BillingAccessPolicy.IsBlockedForBilling(tenant, UserActorType.PlatformAdmin));
    }

    [Fact]
    public void Unblocked_tenant_never_blocks()
    {
        var tenant = BuildTenant(blocked: false);

        Assert.False(BillingAccessPolicy.IsBlockedForBilling(tenant, UserActorType.TenantEmployee));
        Assert.False(BillingAccessPolicy.IsBlockedForBilling(tenant, UserActorType.CustomerPortal));
    }

    [Fact]
    public async Task Expired_status_blocks_access_and_revokes_all_sessions()
    {
        var tenantId = Guid.NewGuid();
        var tenants = new RecordingTenantRegistry();
        var sessions = new RecordingSessionRepository();
        var audit = new FakeAuthAuditWriter();
        var metrics = new FakeSubscriptionAccessMetrics();

        await TenantSubscriptionAccessConsumer.Handle(
            StatusEvent(tenantId, "Expired"),
            tenants,
            sessions,
            audit,
            metrics,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<TenantSubscriptionStatusChangedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.True(tenants.Blocked);
        Assert.Equal("Expired", tenants.Reason);
        Assert.Equal("subscription_lapsed", sessions.RevokedReason);
        Assert.Equal("auth.subscription.access_blocked", Assert.Single(audit.Logs).Action);
        Assert.Equal("Expired", Assert.Single(metrics.Blocked));
    }

    [Fact]
    public async Task Active_status_restores_access_without_revoking()
    {
        var tenantId = Guid.NewGuid();
        var tenants = new RecordingTenantRegistry();
        var sessions = new RecordingSessionRepository();

        await TenantSubscriptionAccessConsumer.Handle(
            StatusEvent(tenantId, "Active"),
            tenants,
            sessions,
            new FakeAuthAuditWriter(),
            new FakeSubscriptionAccessMetrics(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<TenantSubscriptionStatusChangedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.False(tenants.Blocked);
        Assert.Null(sessions.RevokedReason);
    }

    [Fact]
    public async Task Past_due_does_not_block_or_revoke()
    {
        var tenants = new RecordingTenantRegistry();
        var sessions = new RecordingSessionRepository();

        await TenantSubscriptionAccessConsumer.Handle(
            StatusEvent(Guid.NewGuid(), "PastDue"),
            tenants,
            sessions,
            new FakeAuthAuditWriter(),
            new FakeSubscriptionAccessMetrics(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<TenantSubscriptionStatusChangedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Null(tenants.LastCall);
        Assert.Null(sessions.RevokedReason);
    }

    private static TenantSubscriptionStatusChangedIntegrationEvent StatusEvent(Guid tenantId, string status) =>
        new()
        {
            TenantId = tenantId,
            TenantSubscriptionId = Guid.NewGuid(),
            Status = status,
            PreviousStatus = "GracePeriod",
            Reason = nameof(SubscriptionChangeReason.GraceExpired),
        };

    private static Tenant BuildTenant(bool blocked)
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Office", "office", TenantKind.Customer, "Etc/UTC").Value;
        if (blocked)
            tenant.SetBillingAccess(true, "Expired");
        return tenant;
    }

    private sealed class RecordingTenantRegistry : ITenantRegistry
    {
        public bool Blocked { get; private set; }
        public string? Reason { get; private set; }
        public string? LastCall { get; private set; }

        public Task SetBillingBlockedAsync(Guid tenantId, bool blocked, string? reason, CancellationToken ct = default)
        {
            LastCall = "billing";
            Blocked = blocked;
            Reason = reason;
            return Task.CompletedTask;
        }

        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingSessionRepository : ISessionRepository
    {
        public string? RevokedReason { get; private set; }

        public Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default)
        {
            RevokedReason = reason;
            return Task.FromResult(3);
        }

        public Task AddSessionAsync(UserSession session, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<UserSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<UserSession>> GetActiveSessionsByUserAsync(
            Guid userId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddTokenAsync(RefreshToken token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<RefreshToken?> GetTokenByHashAsync(string tokenHash, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> RevokeAllForUserAsync(
            Guid userId,
            string reason,
            Guid? exceptSessionId = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeSubscriptionAccessMetrics : ISubscriptionAccessMetrics
    {
        public List<string> Blocked { get; } = [];
        public int Restored { get; private set; }

        public void RecordAccessBlocked(string status) => Blocked.Add(status);

        public void RecordAccessRestored() => Restored++;
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose() { }
        }
    }
}
