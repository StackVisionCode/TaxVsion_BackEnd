using TaxVision.Gateway.LoadShedding;
using Xunit;

namespace TaxVision.Gateway.Tests.LoadShedding;

public sealed class TenantConsumptionTrackerTests
{
    [Fact]
    public void GetTopConsumers_OrdersDescendingByRequestCount()
    {
        var tracker = new TenantConsumptionTracker(60);

        for (var i = 0; i < 5; i++)
            tracker.RecordRequest("tenant-a");
        for (var i = 0; i < 2; i++)
            tracker.RecordRequest("tenant-b");
        tracker.RecordRequest("tenant-c");

        var top = tracker.GetTopConsumers(topN: 10);

        Assert.Equal(3, top.Count);
        Assert.Equal("tenant-a", top[0].TenantKey);
        Assert.Equal(5, top[0].RequestCount);
        Assert.Equal("tenant-b", top[1].TenantKey);
        Assert.Equal("tenant-c", top[2].TenantKey);
    }

    [Fact]
    public void GetTopConsumers_RespectsTopNLimit()
    {
        var tracker = new TenantConsumptionTracker(60);

        for (var i = 0; i < 20; i++)
            tracker.RecordRequest($"tenant-{i}");

        var top = tracker.GetTopConsumers(topN: 3);

        Assert.Equal(3, top.Count);
    }

    [Fact]
    public void GetTopConsumers_WithNoRequests_ReturnsEmpty()
    {
        var tracker = new TenantConsumptionTracker(60);

        var top = tracker.GetTopConsumers(topN: 10);

        Assert.Empty(top);
    }
}
