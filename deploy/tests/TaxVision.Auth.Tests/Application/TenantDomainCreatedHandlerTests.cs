using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.TenantDomains.DomainEvents;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase A7 — handler local del domain event TenantDomainCreated: audita y publica el integration event correspondiente.</summary>
public sealed class TenantDomainCreatedHandlerTests
{
    [Fact]
    public async Task Handle_writes_audit_log_and_publishes_integration_event()
    {
        var tenantId = Guid.NewGuid();
        var domainId = Guid.NewGuid();
        var actingUserId = Guid.NewGuid();
        var audit = new FakeAuthAuditWriter();
        var bus = new FakeMessageBus();

        await TenantDomainCreatedHandler.Handle(
            new TenantDomainCreated(tenantId, domainId, "archivos.suoficina.com", "CustomHostname", actingUserId),
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            bus,
            CancellationToken.None
        );

        var log = Assert.Single(audit.Logs);
        Assert.Equal(AuthAuditAction.TenantDomainCreated, log.Action);
        Assert.True(log.Success);
        Assert.Equal(actingUserId, log.UserId);
        Assert.Equal(domainId, log.TargetId);

        var published = Assert.Single(bus.Published.OfType<TenantDomainCreatedIntegrationEvent>());
        Assert.Equal(tenantId, published.TenantId);
        Assert.Equal(domainId, published.DomainId);
        Assert.Equal("archivos.suoficina.com", published.Host);
        Assert.Equal("CustomHostname", published.DomainType);
    }
}
