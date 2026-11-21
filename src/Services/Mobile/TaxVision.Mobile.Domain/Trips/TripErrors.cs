using BuildingBlocks.Results;

namespace TaxVision.Mobile.Domain.Trips;

public static class TripErrors
{
    public static readonly Error NotFound = new("Trip.NotFound", "Trip was not found.");
    public static readonly Error Invalid = new(
        "Trip.InvalidInput",
        "Invalid trip, purpose, timestamp, notes or GPS batch."
    );
    public static readonly Error Sequence = new(
        "Trip.SequenceConflict",
        "Operation or point sequence is out of order. Reload the trip and retry."
    );
    public static readonly Error Replay = new(
        "Trip.ReplayConflict",
        "This operation sequence was already used with a different payload."
    );
    public static readonly Error State = new(
        "Trip.StateConflict",
        "This operation is not allowed in the current trip state."
    );
    public static readonly Error VehicleInUse = new(
        "Vehicle.InUse",
        "This vehicle has trips. Preserve it for trip history."
    );
    public static readonly Error InvalidRetention = new(
        "Mobile.Retention.InvalidDays",
        "Retention must be between 1 and 3650 days, or empty to use the default period."
    );
    public static readonly Error InvalidRate = new(
        "MileageRate.InvalidInput",
        "Invalid purpose, effective date, currency or rate."
    );
}
