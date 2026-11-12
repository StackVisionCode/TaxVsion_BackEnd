using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.ValueObjects;

/// <summary>Posición del plan en la vitrina: menor sale primero. También ordena los planes en la consola.</summary>
public sealed record SalesRank
{
    public const int Min = 1;
    public const int Max = 1000;

    public static readonly SalesRank Default = new(100);

    public int Value { get; }

    private SalesRank(int value) => Value = value;

    public static Result<SalesRank> Create(int value)
    {
        if (value is < Min or > Max)
            return Result.Failure<SalesRank>(
                new Error("SalesRank.Invalid", $"Sales rank must be between {Min} and {Max}.")
            );

        return Result.Success(new SalesRank(value));
    }
}
