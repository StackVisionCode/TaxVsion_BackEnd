using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record TripFilter(
    int Page = 1,
    int Size = 20,
    DateTimeOffset? StartDate = null,
    DateTimeOffset? EndDate = null,
    string? Purpose = null,
    TripStatus? Status = null,
    Guid? VehicleId = null,
    double? MinDistanceMiles = null,
    double? MaxDistanceMiles = null,
    string SortBy = "date",
    string SortOrder = "desc"
);
