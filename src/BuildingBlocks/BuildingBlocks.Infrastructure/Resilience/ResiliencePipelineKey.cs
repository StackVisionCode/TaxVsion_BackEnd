namespace BuildingBlocks.Infrastructure.Resilience;

public readonly record struct ResiliencePipelineKey
{
    public string Value { get; }

    private ResiliencePipelineKey(string value) => Value = value;

    public static ResiliencePipelineKey From(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Resilience pipeline key cannot be blank.", nameof(value))
            : new ResiliencePipelineKey(value.Trim());

    public override string ToString() => Value;
}
