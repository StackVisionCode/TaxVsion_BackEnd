using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record MileageConfigurationResponse(
    DateOnly Date,
    string Purpose,
    string DistanceUnit,
    Guid? RateId,
    decimal? MileageRate,
    string? Currency,
    bool IsConfigured
);
