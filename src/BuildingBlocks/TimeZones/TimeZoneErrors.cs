using BuildingBlocks.Results;

namespace BuildingBlocks.TimeZones;

public static class TimeZoneErrors
{
    public static readonly Error NonexistentLocalTime = new(
        "TimeZone.NonexistentLocalTime",
        "That local time does not exist in the selected time zone because of a daylight saving change."
    );
}
