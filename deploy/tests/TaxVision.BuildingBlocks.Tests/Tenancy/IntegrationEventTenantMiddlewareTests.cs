using BuildingBlocks.Messaging;
using BuildingBlocks.Tenancy;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Tenancy;

public sealed class IntegrationEventTenantMiddlewareTests
{
    [Fact]
    public void Stamps_bus_TenantId_from_the_event_payload_so_a_nested_publish_inherits_the_tenant()
    {
        var tenantId = Guid.NewGuid();
        var message = new FakeIntegrationEvent { TenantId = tenantId };
        var tenantContext = new TenantContext();
        var bus = new FakeMessageBus();

        IntegrationEventTenantMiddleware.Before(message, tenantContext, bus);

        Assert.True(tenantContext.HasTenant);
        Assert.Equal(tenantId, tenantContext.TenantId);
        Assert.Equal(tenantId.ToString(), bus.TenantId);
    }

    [Fact]
    public void Throws_when_the_event_payload_carries_no_tenant()
    {
        var message = new FakeIntegrationEvent { TenantId = Guid.Empty };
        var tenantContext = new TenantContext();
        var bus = new FakeMessageBus();

        Assert.Throws<InvalidOperationException>(() =>
            IntegrationEventTenantMiddleware.Before(message, tenantContext, bus)
        );
        Assert.Null(bus.TenantId);
    }

    private sealed record FakeIntegrationEvent : IntegrationEvent;
}
