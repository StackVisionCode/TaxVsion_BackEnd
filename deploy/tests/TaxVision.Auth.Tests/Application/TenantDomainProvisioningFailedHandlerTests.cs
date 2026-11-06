using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.TenantDomains.DomainEvents;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase A7 — handler local del domain event TenantDomainProvisioningFailed: audita una vez y devuelve el integration event correspondiente (Wolverine lo manda por el outbox).</summary>
public sealed class TenantDomainProvisioningFailedHandlerTests
{
    [Fact]
    public async Task Handle_writes_audit_log_once_and_returns_integration_event()
    {
        var tenantId = Guid.NewGuid();
        var domainId = Guid.NewGuid();
        var audit = new FakeAuthAuditWriter();

        var published = await TenantDomainProvisioningFailedHandler.Handle(
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
            CancellationToken.None
        );

        var log = Assert.Single(audit.Logs);
        Assert.Equal(AuthAuditAction.TenantDomainProvisioningFailed, log.Action);
        Assert.False(log.Success);
        Assert.Null(log.UserId);
        Assert.Contains("cloudflare_blocked", log.DetailsJson);

        Assert.Equal(domainId, published.DomainId);
        Assert.Equal("cloudflare_blocked", published.Reason);
    }
}
