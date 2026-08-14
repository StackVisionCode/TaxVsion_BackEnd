using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.ValueObjects;

/// <summary>Previsión del usuario, no el tiempo imputado — eso sale de los timers cerrados.</summary>
public sealed record EstimatedHours
{
    /// <summary>Dos decimales: los cuartos de hora se representan exacto.</summary>
    public const int Scale = 2;

    public const decimal MaxValue = 9_999.99m;

    public decimal Value { get; }

    private EstimatedHours(decimal value) => Value = value;

    public static Result<EstimatedHours> Create(decimal value)
    {
        if (value <= 0m)
            return Result.Failure<EstimatedHours>(TaskErrors.EstimatedHoursNotPositive);

        if (value > MaxValue)
            return Result.Failure<EstimatedHours>(TaskErrors.EstimatedHoursTooLarge);

        return Result.Success(new EstimatedHours(decimal.Round(value, Scale, MidpointRounding.AwayFromZero)));
    }
}
