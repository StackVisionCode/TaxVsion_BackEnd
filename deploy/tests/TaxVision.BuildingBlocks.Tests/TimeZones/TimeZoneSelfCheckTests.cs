using BuildingBlocks.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.TimeZones;

public sealed class TimeZoneSelfCheckTests
{
    [Fact]
    public void Lists_the_probe_zones_that_do_not_resolve()
    {
        Assert.Equal(["Asia/Tokyo"], TimeZoneSelfCheck.MissingZones(id => id != "Asia/Tokyo"));
        Assert.Empty(TimeZoneSelfCheck.MissingZones(_ => true));
    }

    [Fact]
    public async Task On_start_it_only_logs_and_reports_the_probes()
    {
        var logger = new RecordingLogger();
        await new TimeZoneSelfCheck(logger).StartAsync(CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("America/New_York", entry.Message);
        Assert.Contains("Asia/Tokyo", entry.Message);
    }

    [Fact]
    public void Registers_as_a_hosted_service()
    {
        var services = new ServiceCollection().AddLogging().AddTimeZoneSelfCheck();
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(TimeZoneSelfCheck)
        );
    }

    private sealed class RecordingLogger : ILogger<TimeZoneSelfCheck>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
