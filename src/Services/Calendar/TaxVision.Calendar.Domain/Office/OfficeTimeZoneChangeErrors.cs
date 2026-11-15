using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Office;

public static class OfficeTimeZoneChangeErrors
{
    public static readonly Error NotPending = new(
        "Calendar.OfficeTimeZoneChange.NotPending",
        "This time zone change was already handled or replaced by a newer one."
    );

    public static readonly Error NotMovable = new(
        "Calendar.OfficeTimeZoneChange.NotMovable",
        "Only working hours and recurring appointments in the previous time zone can be moved."
    );
}
