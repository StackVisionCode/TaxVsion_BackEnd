namespace TaxVision.Auth.Domain.Sessions;

/// <summary>
/// Una sesión web sigue viva si empezó después de <see cref="StartedAfterUtc"/> y se vio desde
/// <see cref="SeenSinceUtc"/>. La app móvil tiene su propio ciclo y siempre queda dentro.
/// </summary>
public sealed record SessionActivityWindow(DateTime StartedAfterUtc, DateTime SeenSinceUtc)
{
    public bool Covers(SessionChannel channel, DateTime createdAtUtc, DateTime lastSeenAtUtc) =>
        channel != SessionChannel.Web || (createdAtUtc > StartedAfterUtc && lastSeenAtUtc >= SeenSinceUtc);
}
