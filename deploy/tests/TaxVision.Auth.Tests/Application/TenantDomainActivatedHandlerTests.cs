using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.TenantDomains.DomainEvents;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Fase A7 — handler local del domain event TenantDomainActivated: audita y publica
/// AMBOS integration events (Verified + Activated). Es el mismo handler tanto si lo
/// disparó la activación manual como el poller automático.
/// </summary>
public sealed class TenantDomainActivatedHandlerTests
{
    [Fact]
    public async Task Handle_writes_audit_log_and_publishes_verified_and_activated()
    {
        var tenantId = Guid.NewGuid();
        var domainId = Guid.NewGuid();
        var audit = new FakeAuthAuditWriter();
        var bus = new FakeMessageBus();

        await TenantDomainActivatedHandler.Handle(
            new TenantDomainActivated(tenantId, domainId, "archivos.suoficina.com", null),
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            bus,
            CancellationToken.None
        );

        var log = Assert.Single(audit.Logs);
        Assert.Equal(AuthAuditAction.TenantDomainActivated, log.Action);
        Assert.True(log.Success);
        Assert.Null(log.UserId); // disparado por el poller, sin actor humano

        Assert.Single(bus.Published.OfType<TenantDomainVerifiedIntegrationEvent>());
        Assert.Single(bus.Published.OfType<TenantDomainActivatedIntegrationEvent>());
    }
}
