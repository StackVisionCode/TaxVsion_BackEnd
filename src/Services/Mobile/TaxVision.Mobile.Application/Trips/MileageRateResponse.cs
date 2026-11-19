using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record MileageRateResponse(
    Guid Id,
    string Purpose,
    DateOnly EffectiveFrom,
    decimal RatePerMile,
    string Currency,
    DateTime CreatedAt
)
{
    public static MileageRateResponse From(MileageRate r) =>
        new(r.Id, r.Purpose, r.EffectiveFrom, r.RatePerMile, r.Currency, r.CreatedAtUtc);
}
