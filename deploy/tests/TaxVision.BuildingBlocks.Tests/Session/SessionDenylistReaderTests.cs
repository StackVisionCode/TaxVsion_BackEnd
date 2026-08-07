using BuildingBlocks.Caching;
using BuildingBlocks.Infrastructure.Sessions;
using BuildingBlocks.Sessions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Session;

public sealed class SessionDenylistReaderTests
{
    [Fact]
    public async Task Senala_el_fallo_en_vez_de_tragarselo_cuando_el_cache_no_responde()
    {
        // H-06 — antes devolvía false (fail-open quemado en el adaptador). Ahora la política la
        // decide SessionDenylistMiddleware, así que el reader tiene que dejar ver el fallo.
        var reader = new SessionDenylistReader(new ThrowingCacheService());

        var sessionId = Guid.NewGuid();
        var exception = await Assert.ThrowsAsync<SessionDenylistUnavailableException>(() =>
            reader.IsSessionDeniedAsync(sessionId)
        );

        Assert.Equal(sessionId, exception.SessionId);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Returns_true_when_the_cache_has_the_session_marked_as_denied()
    {
        var reader = new SessionDenylistReader(new FakeCacheService(true));

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
}
