namespace TaxVision.Growth.Infrastructure.Idempotency;

public sealed class BusinessIdempotencyOptions
{
    public const string SectionName = "Growth:BusinessIdempotency";

    public int RetentionDays { get; init; } = 2557;
}
