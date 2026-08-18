namespace BuildingBlocks.TimeZones;

public static class IanaTimeZone
{
    public const string UtcId = "Etc/UTC";

    public static bool TryNormalize(string? timeZoneId, out string normalizedTimeZoneId)
    {
        normalizedTimeZoneId = timeZoneId?.Trim() ?? string.Empty;
        return TryFindTimeZone(normalizedTimeZoneId, out _);
    }

    /// <summary>
    /// Igual que <see cref="TryNormalize"/> pero devuelve la zona ya resuelta, para quien necesita
    /// <b>convertir</b> una hora y no solo validar el id. Vive acá y no en cada llamador porque el
    /// mapeo IANA↔Windows es justo la parte que se hace mal cuando se reescribe.
    /// </summary>
    public static bool TryFindTimeZone(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;

        var normalized = timeZoneId?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || !TimeZoneInfo.TryConvertIanaIdToWindowsId(normalized, out var windowsTimeZoneId))
            return false;

        try
        {
            var resolvableTimeZoneId = OperatingSystem.IsWindows() ? windowsTimeZoneId : normalized;

            timeZone = TimeZoneInfo.FindSystemTimeZoneById(resolvableTimeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
