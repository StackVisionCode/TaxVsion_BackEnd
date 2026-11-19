using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Mobile.Domain.Trips;

public sealed class MileageRate : TenantEntity
{
    private MileageRate() { }

    public string Purpose { get; private set; } = string.Empty;
    public DateOnly EffectiveFrom { get; private set; }
    public decimal RatePerMile { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<MileageRate> Create(
        Guid tenant,
        string purpose,
        DateOnly effectiveFrom,
        decimal rate,
        string? currency,
        DateTime nowUtc
    )
    {
        if (
            tenant == Guid.Empty
            || !TripPurpose.IsValid(purpose)
            || effectiveFrom < new DateOnly(1970, 1, 1)
            || effectiveFrom < DateOnly.FromDateTime(nowUtc)
            || rate < 0
            || rate > 1000
            || decimal.Round(rate, 4) != rate
            || currency is null
            || currency.Length != 3
        )
            return Result.Failure<MileageRate>(TripErrors.InvalidRate);
        foreach (var letter in currency)
            if (letter is < 'A' or > 'Z')
                return Result.Failure<MileageRate>(TripErrors.InvalidRate);
        var value = new MileageRate
        {
            Id = Guid.NewGuid(),
            Purpose = purpose,
            EffectiveFrom = effectiveFrom,
            RatePerMile = rate,
            Currency = currency,
            CreatedAtUtc = nowUtc,
        };
        value.SetTenant(tenant);
        return Result.Success(value);
    }
}
