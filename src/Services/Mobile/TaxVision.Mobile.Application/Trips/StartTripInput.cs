using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record StartTripInput(
    Guid ClientTripId,
    Guid VehicleId,
    string Purpose,
    DateTimeOffset StartedAt,
    GpsPointInput? StartPoint,
    string? Notes = null
);
