namespace BuildingBlocks.Infrastructure.RateLimiting;

public readonly record struct RateCounterKey
{
    public string Value { get; }

    private RateCounterKey(string value) => Value = value;

    public static RateCounterKey From(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Rate counter key cannot be blank.", nameof(value))
            : new RateCounterKey(value.Trim());

    public override string ToString() => Value;
}
