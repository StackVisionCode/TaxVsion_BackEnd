using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.TenantDomains.DomainEvents;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase A7 — handler local del domain event TenantDomainDisabled: audita y publica el integration event correspondiente.</summary>
public sealed class TenantDomainDisabledHandlerTests
{
    [Fact]
    public async Task Handle_writes_audit_log_and_publishes_integration_event()
    {
        var tenantId = Guid.NewGuid();
        var domainId = Guid.NewGuid();
        var actingUserId = Guid.NewGuid();
        var audit = new FakeAuthAuditWriter();
        var bus = new FakeMessageBus();

        await TenantDomainDisabledHandler.Handle(
            new TenantDomainDisabled(tenantId, domainId, "archivos.suoficina.com", actingUserId),
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            bus,
            CancellationToken.None
        );

        var log = Assert.Single(audit.Logs);
        Assert.Equal(AuthAuditAction.TenantDomainDisabled, log.Action);
        Assert.Equal(actingUserId, log.UserId);

        var published = Assert.Single(bus.Published.OfType<TenantDomainDisabledIntegrationEvent>());
        Assert.Equal(domainId, published.DomainId);
    }
}
