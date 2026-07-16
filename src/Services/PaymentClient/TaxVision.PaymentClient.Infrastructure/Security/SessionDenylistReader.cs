using BuildingBlocks.Caching;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Infrastructure.Security;

/// <summary>Lee la misma clave Redis que <c>Auth.Infrastructure.Security.AccessTokenDenylist</c>
/// escribe — PaymentClient comparte el store, no el escritor.</summary>
public sealed class SessionDenylistReader(ICacheService cache) : ISessionDenylistReader
{
    private static string Key(Guid sessionId) => $"auth:denylist:sid:{sessionId:N}";

    public async Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) =>
        await cache.GetAsync<bool?>(Key(sessionId), ct) == true;
}
