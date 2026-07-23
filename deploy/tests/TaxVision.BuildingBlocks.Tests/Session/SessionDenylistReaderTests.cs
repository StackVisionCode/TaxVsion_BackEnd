using BuildingBlocks.Caching;
using BuildingBlocks.Sessions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Session;

public sealed class SessionDenylistReaderTests
{
    [Fact]
    public async Task Fails_open_and_logs_a_warning_when_the_cache_is_unavailable()
    {
        var logger = new RecordingLogger<SessionDenylistReader>();
        var reader = new SessionDenylistReader(new ThrowingCacheService(), logger);

        var isDenied = await reader.IsSessionDeniedAsync(Guid.NewGuid());

        Assert.False(isDenied);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Returns_true_when_the_cache_has_the_session_marked_as_denied()
    {
        var reader = new SessionDenylistReader(
            new FakeCacheService(true),
            new RecordingLogger<SessionDenylistReader>()
        );

        Assert.True(await reader.IsSessionDeniedAsync(Guid.NewGuid()));
    }

    private sealed class ThrowingCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            throw new InvalidOperationException("Redis unavailable (simulated).");

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeCacheService(bool? denied) : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult((T?)(object?)denied);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
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
