namespace BuildingBlocks.TimeZones;

/// <summary>
/// Zona IANA ya resuelta para calcular. <see cref="IsFallback"/> avisa que no había una zona válida y se usó UTC:
/// quien la pidió decide si lo registra; nunca es silencioso para él.
/// </summary>
public sealed record ResolvedTimeZone(string Id, TimeZoneInfo Zone, bool IsFallback)
{
    public static ResolvedTimeZone Utc { get; } = new(IanaTimeZone.UtcId, TimeZoneInfo.Utc, false);

    public static ResolvedTimeZone Resolve(string? timeZoneId) =>
        IanaTimeZone.TryFindTimeZone(timeZoneId, out var zone)
            ? new ResolvedTimeZone(timeZoneId!.Trim(), zone, false)
            : new ResolvedTimeZone(IanaTimeZone.UtcId, TimeZoneInfo.Utc, true);
}
