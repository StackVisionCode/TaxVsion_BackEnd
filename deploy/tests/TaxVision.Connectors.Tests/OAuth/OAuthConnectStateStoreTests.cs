using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.OAuth;

namespace TaxVision.Connectors.Tests.OAuth;

public class OAuthConnectStateStoreTests
{
    [Fact]
    public async Task CreateAndConsume_RoundTripsTheOriginalData()
    {
        var store = new InMemoryOAuthConnectStateStore();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var state = await store.CreateAsync(tenantId, ProviderCode.Gmail, userId);
        var consumed = await store.ConsumeAsync(state);

        Assert.NotNull(consumed);
        Assert.Equal(tenantId, consumed!.TenantId);
        Assert.Equal(ProviderCode.Gmail, consumed.ProviderCode);
        Assert.Equal(userId, consumed.InitiatedByUserId);
    }

    [Fact]
    public async Task Consume_CanOnlyBeCalledOnce()
    {
        var store = new InMemoryOAuthConnectStateStore();
        var state = await store.CreateAsync(Guid.NewGuid(), ProviderCode.Graph, Guid.NewGuid());

        var first = await store.ConsumeAsync(state);
        var second = await store.ConsumeAsync(state);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task Consume_WithUnknownState_ReturnsNull()
    {
        var store = new InMemoryOAuthConnectStateStore();

        var consumed = await store.ConsumeAsync("forged-state-nobody-created");

        Assert.Null(consumed);
    }

    [Fact]
    public async Task Create_ReturnsDistinctStatesAcrossCalls()
    {
        var store = new InMemoryOAuthConnectStateStore();
        var tenantId = Guid.NewGuid();

        var first = await store.CreateAsync(tenantId, ProviderCode.Gmail, Guid.NewGuid());
        var second = await store.CreateAsync(tenantId, ProviderCode.Gmail, Guid.NewGuid());

        Assert.NotEqual(first, second);
    }
}
