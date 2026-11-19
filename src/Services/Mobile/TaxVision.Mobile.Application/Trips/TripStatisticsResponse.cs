using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record TripStatisticsResponse(
    int TotalTrips,
    double TotalMiles,
    double TotalKilometers,
    double TotalDurationSeconds,
    int TripsThisWeek,
    double MilesThisWeek,
    int TripsThisMonth,
    double MilesThisMonth,
    int TripsThisYear,
    double MilesThisYear,
    double AverageTripMiles,
    double AverageTripDuration,
    IReadOnlyList<PurposeMileage> MilesByPurpose,
    IReadOnlyList<CurrencyEstimate> EstimatedAmounts,
    int TripsWithoutRate,
    string TimeZone
);
