using BuildingBlocks.Tenancy;
using Wolverine;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Tenancy;

public sealed class LocalCommandTenantMiddlewareTests
{
    private sealed record FakeScanFileCommand(Guid TenantId, Guid FileId);

    private sealed record FakeMessageWithoutTenant(Guid FileId);

    [Fact]
    public void Reads_TenantId_from_the_message_itself_as_the_primary_source()
    {
        var tenantId = Guid.NewGuid();
        var envelope = new Envelope { Message = new FakeScanFileCommand(tenantId, Guid.NewGuid()) };
        var tenantContext = new TenantContext();
        var bus = new FakeMessageBus();

        LocalCommandTenantMiddleware.Before(envelope, tenantContext, bus);

        Assert.True(tenantContext.HasTenant);
        Assert.Equal(tenantId, tenantContext.TenantId);
        Assert.Equal(tenantId.ToString(), bus.TenantId);
    }

    [Fact]
    public void Falls_back_to_envelope_TenantId_when_the_message_has_no_TenantId_property()
    {
        var tenantId = Guid.NewGuid();
        var envelope = new Envelope
        {
            Message = new FakeMessageWithoutTenant(Guid.NewGuid()),
            TenantId = tenantId.ToString(),
        };
        var tenantContext = new TenantContext();
        var bus = new FakeMessageBus();

        LocalCommandTenantMiddleware.Before(envelope, tenantContext, bus);

        Assert.True(tenantContext.HasTenant);
        Assert.Equal(tenantId, tenantContext.TenantId);
        Assert.Equal(tenantId.ToString(), bus.TenantId);
    }

    [Fact]
    public void Prefers_the_message_TenantId_over_a_stale_envelope_TenantId()
    {
        var messageTenantId = Guid.NewGuid();
        var envelope = new Envelope
        {
            Message = new FakeScanFileCommand(messageTenantId, Guid.NewGuid()),
            TenantId = Guid.NewGuid().ToString(),
        };
        var tenantContext = new TenantContext();
        var bus = new FakeMessageBus();

        LocalCommandTenantMiddleware.Before(envelope, tenantContext, bus);

        Assert.Equal(messageTenantId, tenantContext.TenantId);
        Assert.Equal(messageTenantId.ToString(), bus.TenantId);
    }

    [Fact]
    public void Does_not_touch_tenant_state_when_neither_message_nor_envelope_carry_a_tenant()
    {
        var envelope = new Envelope { Message = new FakeMessageWithoutTenant(Guid.NewGuid()) };
        var tenantContext = new TenantContext();
        var bus = new FakeMessageBus();

        LocalCommandTenantMiddleware.Before(envelope, tenantContext, bus);

        Assert.False(tenantContext.HasTenant);
        Assert.Null(bus.TenantId);
    }
}
