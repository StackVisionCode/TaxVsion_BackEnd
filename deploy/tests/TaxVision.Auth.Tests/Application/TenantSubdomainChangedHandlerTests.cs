using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains.DomainEvents;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Fase A7 — handler local del domain event TenantSubdomainChanged: audita, publica el integration
/// event (con los slugs, que es lo que guardan las réplicas), actualiza la réplica local del tenant si
/// cambió el primario y olvida el host viejo en la cache de resolución.
/// </summary>
public sealed class TenantSubdomainChangedHandlerTests
{
    private sealed class FakeTenantRegistry(Tenant? tenant) : ITenantRegistry
    {
        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(tenant?.Id == tenantId ? tenant : null);

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetBillingBlockedAsync(
            Guid tenantId,
            bool blocked,
            string? reason,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private static TenantSubdomainChanged Changed(Guid tenantId, Guid domainId, Guid actingUserId, bool isPrimary) =>
        new(
            tenantId,
            domainId,
            "oficina1.taxproffice.com",
            "oficina2.taxproffice.com",
            actingUserId,
            "oficina1",
            "oficina2",
            isPrimary
        );

    [Fact]
    public async Task Handle_writes_audit_log_and_publishes_integration_event()
    {
        var tenantId = Guid.NewGuid();
        var domainId = Guid.NewGuid();
        var actingUserId = Guid.NewGuid();
        var audit = new FakeAuthAuditWriter();
        var bus = new FakeMessageBus();

        await TenantSubdomainChangedHandler.Handle(
            Changed(tenantId, domainId, actingUserId, isPrimary: true),
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            bus,
            new FakeTenantRegistry(null),
            new RecordingResolutionCache(),
            CancellationToken.None
        );

        var log = Assert.Single(audit.Logs);
        Assert.Equal(AuthAuditAction.TenantSubdomainChanged, log.Action);
        Assert.True(log.Success);
        Assert.Equal(actingUserId, log.UserId);
        Assert.Contains("oficina1.taxproffice.com", log.DetailsJson);
        Assert.Contains("oficina2.taxproffice.com", log.DetailsJson);

        var published = Assert.Single(bus.Published.OfType<TenantSubdomainChangedIntegrationEvent>());
        Assert.Equal(domainId, published.DomainId);
        Assert.Equal("oficina1.taxproffice.com", published.OldHost);
        Assert.Equal("oficina2.taxproffice.com", published.NewHost);
        Assert.Equal("oficina1", published.OldSlug);
        Assert.Equal("oficina2", published.NewSlug);
        Assert.True(published.IsPrimary);
    }

    [Fact]
    public async Task A_primary_rename_updates_the_local_tenant_slug_and_forgets_the_old_host()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Tenant.Register(tenantId, "Oficina", "oficina1", TenantKind.Customer, "Etc/UTC").Value;
        var cache = new RecordingResolutionCache();

        await TenantSubdomainChangedHandler.Handle(
            Changed(tenantId, Guid.NewGuid(), Guid.NewGuid(), isPrimary: true),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeMessageBus(),
            new FakeTenantRegistry(tenant),
            cache,
            CancellationToken.None
        );

        Assert.Equal("oficina2", tenant.SubDomain);
        Assert.Equal(["oficina1.taxproffice.com"], cache.Invalidated);
    }

    [Fact]
    public async Task A_secondary_subdomain_rename_leaves_the_tenant_slug_alone()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Tenant.Register(tenantId, "Oficina", "oficina1", TenantKind.Customer, "Etc/UTC").Value;

        await TenantSubdomainChangedHandler.Handle(
            Changed(tenantId, Guid.NewGuid(), Guid.NewGuid(), isPrimary: false),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeMessageBus(),
            new FakeTenantRegistry(tenant),
            new RecordingResolutionCache(),
            CancellationToken.None
        );

        Assert.Equal("oficina1", tenant.SubDomain);
    }
}
