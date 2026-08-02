using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Gateway.LoadShedding;
using Xunit;

namespace TaxVision.Gateway.Tests.LoadShedding;

public sealed class LoadShedderTests
{
    private static LoadShedder CreateShedder(
        RequestOutcomeWindow window,
        TenantConsumptionTracker tracker,
        LoadShedderOptions? options = null
    ) =>
        new(
            window,
            tracker,
            Options.Create(options ?? new LoadShedderOptions { MinSamples = 1 }),
            NullLogger<LoadShedder>.Instance
        );

    [Fact]
    public void ShouldShed_ReturnsFalse_WhenDisabled()
    {
        var window = new RequestOutcomeWindow(60);
        var tracker = new TenantConsumptionTracker(60);
        for (var i = 0; i < 5; i++)
            window.Record(5000, 500); // clearly overloaded latency + errors
        var shedder = CreateShedder(window, tracker, new LoadShedderOptions { Enabled = false, MinSamples = 1 });

        Assert.False(shedder.ShouldShed("tenant-a"));
    }

    [Fact]
    public void ShouldShed_ReturnsFalse_WhenBelowMinSamples()
    {
        var window = new RequestOutcomeWindow(60);
        var tracker = new TenantConsumptionTracker(60);
        window.Record(5000, 500);
        var shedder = CreateShedder(window, tracker, new LoadShedderOptions { MinSamples = 50 });

        Assert.False(shedder.ShouldShed("tenant-a"));
    }

    [Fact]
    public void ShouldShed_ReturnsFalse_WhenNotOverloaded()
    {
        var window = new RequestOutcomeWindow(60);
        var tracker = new TenantConsumptionTracker(60);
        for (var i = 0; i < 30; i++)
            window.Record(50, 200); // fast, all 2xx
        var shedder = CreateShedder(window, tracker);

        tracker.RecordRequest("tenant-a");

        Assert.False(shedder.ShouldShed("tenant-a"));
    }

    [Fact]
    public void ShouldShed_PrioritizesTopConsumerTenants_WhenOverloaded()
    {
        var window = new RequestOutcomeWindow(60);
        var tracker = new TenantConsumptionTracker(60);
        for (var i = 0; i < 30; i++)
            window.Record(5000, 200); // p99 way above default threshold -> overloaded

        var shedder = CreateShedder(window, tracker, new LoadShedderOptions { MinSamples = 1, TopConsumerCount = 1 });

        for (var i = 0; i < 100; i++)
            tracker.RecordRequest("heavy-tenant");
        tracker.RecordRequest("light-tenant");

        Assert.True(shedder.ShouldShed("heavy-tenant"));
        Assert.False(shedder.ShouldShed("light-tenant"));
    }

    [Fact]
    public void ShouldShed_TriggersOn5xxErrorRate_EvenWithLowLatency()
    {
        var window = new RequestOutcomeWindow(60);
        var tracker = new TenantConsumptionTracker(60);
        for (var i = 0; i < 10; i++)
            window.Record(10, 503); // fast but all errors

        var shedder = CreateShedder(window, tracker, new LoadShedderOptions { MinSamples = 1, TopConsumerCount = 5 });
        tracker.RecordRequest("tenant-a");

        Assert.True(shedder.ShouldShed("tenant-a"));
    }
}
