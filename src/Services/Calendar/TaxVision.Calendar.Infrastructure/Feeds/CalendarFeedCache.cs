using BuildingBlocks.Caching;
using TaxVision.Calendar.Application.Feeds.Abstractions;

namespace TaxVision.Calendar.Infrastructure.Feeds;

internal sealed class CalendarFeedCache(ICacheService cache) : ICalendarFeedCache
{
    /// <summary>
    /// Un día. Sirve para una caída larga y hace que un feed abandonado desaparezca solo; más allá de
    /// eso, una agenda de ayer engaña más de lo que ayuda.
    /// </summary>
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public Task<string?> GetAsync(string tokenHashHex, CancellationToken ct = default) =>
        cache.GetAsync<string>(KeyOf(tokenHashHex), ct);

    public Task SetAsync(string tokenHashHex, string ics, CancellationToken ct = default) =>
        cache.SetAsync(KeyOf(tokenHashHex), ics, Retention, ct);

    public Task RemoveAsync(string tokenHashHex, CancellationToken ct = default) =>
        cache.RemoveAsync(KeyOf(tokenHashHex), ct);

    /// <summary>Por el hash y nunca por el token: la clave viaja a Redis y aparece en cualquier volcado.</summary>
    private static string KeyOf(string tokenHashHex) => $"calendar:feed:{tokenHashHex}";
}
