using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.TenantDomains.DomainEvents;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase A7 — handler local del domain event TenantSubdomainChanged: audita y publica el integration event correspondiente.</summary>
public sealed class TenantSubdomainChangedHandlerTests
{
    [Fact]
    public async Task Handle_writes_audit_log_and_publishes_integration_event()
    {
        var tenantId = Guid.NewGuid();
        var domainId = Guid.NewGuid();
        var actingUserId = Guid.NewGuid();
        var audit = new FakeAuthAuditWriter();
        var bus = new FakeMessageBus();

        await TenantSubdomainChangedHandler.Handle(
            new TenantSubdomainChanged(
                tenantId,
                domainId,
                "oficina1.taxprocore.com",
                "oficina2.taxprocore.com",
                actingUserId
            ),
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            bus,
            CancellationToken.None
        );

        var log = Assert.Single(audit.Logs);
        Assert.Equal(AuthAuditAction.TenantSubdomainChanged, log.Action);
        Assert.True(log.Success);
        Assert.Equal(actingUserId, log.UserId);
        Assert.Contains("oficina1.taxprocore.com", log.DetailsJson);
        Assert.Contains("oficina2.taxprocore.com", log.DetailsJson);

        var published = Assert.Single(bus.Published.OfType<TenantSubdomainChangedIntegrationEvent>());
        Assert.Equal(domainId, published.DomainId);
        Assert.Equal("oficina1.taxprocore.com", published.OldHost);
        Assert.Equal("oficina2.taxprocore.com", published.NewHost);
    }
}
