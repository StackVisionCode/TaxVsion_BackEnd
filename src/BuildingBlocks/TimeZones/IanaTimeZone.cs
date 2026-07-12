namespace BuildingBlocks.TimeZones;

public static class IanaTimeZone
{
    public const string UtcId = "Etc/UTC";

    public static bool TryNormalize(string? timeZoneId, out string normalizedTimeZoneId)
    {
        normalizedTimeZoneId = timeZoneId?.Trim() ?? string.Empty;
        if (
            normalizedTimeZoneId.Length == 0
            || !TimeZoneInfo.TryConvertIanaIdToWindowsId(normalizedTimeZoneId, out var windowsTimeZoneId)
        )
        {
            return false;
        }

        try
        {
            var resolvableTimeZoneId = OperatingSystem.IsWindows() ? windowsTimeZoneId : normalizedTimeZoneId;

            _ = TimeZoneInfo.FindSystemTimeZoneById(resolvableTimeZoneId);
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
