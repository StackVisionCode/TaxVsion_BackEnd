using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record CreateRateInput(string Purpose, DateOnly EffectiveFrom, decimal RatePerMile, string Currency);
