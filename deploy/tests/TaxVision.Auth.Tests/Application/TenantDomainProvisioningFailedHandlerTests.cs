using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.TenantDomains.DomainEvents;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase A7 — handler local del domain event TenantDomainProvisioningFailed: audita y publica el integration event correspondiente.</summary>
public sealed class TenantDomainProvisioningFailedHandlerTests
{
    [Fact]
    public async Task Handle_writes_audit_log_and_publishes_integration_event()
    {
        var tenantId = Guid.NewGuid();
        var domainId = Guid.NewGuid();
        var audit = new FakeAuthAuditWriter();
        var bus = new FakeMessageBus();

        await TenantDomainProvisioningFailedHandler.Handle(
            new TenantDomainProvisioningFailed(
                tenantId,
                domainId,
                "archivos.suoficina.com",
                "cloudflare_blocked",
                null
            ),
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            bus,
            CancellationToken.None
        );

        var log = Assert.Single(audit.Logs);
        Assert.Equal(AuthAuditAction.TenantDomainProvisioningFailed, log.Action);
        Assert.False(log.Success);
        Assert.Null(log.UserId);
        Assert.Contains("cloudflare_blocked", log.DetailsJson);

        var published = Assert.Single(bus.Published.OfType<TenantDomainProvisioningFailedIntegrationEvent>());
        Assert.Equal(domainId, published.DomainId);
        Assert.Equal("cloudflare_blocked", published.Reason);
    }
}
