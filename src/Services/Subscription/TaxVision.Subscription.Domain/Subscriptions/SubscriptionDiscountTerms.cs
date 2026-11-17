using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Las condiciones de un descuento antes de pegarlo a la suscripción: beneficio (porcentaje o monto fijo), duración,
/// qué cobros cubre y, si vino de un código restringido a un plan, ese plan.
/// </summary>
public sealed record SubscriptionDiscountTerms
{
    public const int MaxMonths = 36;

    private SubscriptionDiscountTerms() { }

    public DiscountSource Source { get; private init; }
    public Guid? CodeDefinitionId { get; private init; }
    public int? PercentBasisPoints { get; private init; }
    public long? AmountCents { get; private init; }
    public string? Currency { get; private init; }
    public DiscountDuration Duration { get; private init; }
    public int? Months { get; private init; }
    public DiscountAppliesTo AppliesTo { get; private init; }
    public string? PlanCode { get; private init; }
    public string? Reason { get; private init; }
    public DateTime StartsAtUtc { get; private init; }

    /// <summary>Código enmascarado (PREFIJO...ÚLTIMOS4) para el recibo; null si no vino de un código.</summary>
    public string? CodeDisplay { get; private init; }

    public const int CodeDisplayMaxLength = 40;

    public static Result<SubscriptionDiscountTerms> Create(
        DiscountSource source,
        Guid? codeDefinitionId,
        int? percentBasisPoints,
        long? amountCents,
        string? currency,
        DiscountDuration duration,
        int? months,
        DiscountAppliesTo appliesTo,
        string? planCode,
        string? reason,
        DateTime startsAtUtc,
        string? codeDisplay = null
    )
    {
        if (!Enum.IsDefined(source) || !Enum.IsDefined(duration) || !Enum.IsDefined(appliesTo))
            return Invalid("Subscription.Discount.Invalid", "The discount terms are invalid.");

        if (percentBasisPoints is not null == amountCents is not null)
            return Invalid("Subscription.Discount.InvalidBenefit", "Use either a percentage or a fixed amount.");

        if (percentBasisPoints is < 1 or > 10_000)
            return Invalid("Subscription.Discount.InvalidPercentage", "The percentage must be between 0.01% and 100%.");

        if (amountCents is not null && (amountCents <= 0 || string.IsNullOrWhiteSpace(currency)))
            return Invalid(
                "Subscription.Discount.InvalidAmount",
                "A fixed amount needs a positive value and a currency."
            );

        if (duration == DiscountDuration.Months ? months is not (>= 1 and <= MaxMonths) : months is not null)
            return Invalid("Subscription.Discount.InvalidMonths", $"Months must be between 1 and {MaxMonths}.");

        if (source == DiscountSource.Admin && string.IsNullOrWhiteSpace(reason))
            return Invalid("Subscription.Discount.ReasonRequired", "A reason is required for an admin discount.");

        if (source == DiscountSource.Code && codeDefinitionId is null)
            return Invalid("Subscription.Discount.CodeRequired", "A code discount needs its code.");

        if (startsAtUtc.Kind != DateTimeKind.Utc)
            return Invalid("Subscription.Discount.NonUtcStart", "The start date must be UTC.");

        var display = string.IsNullOrWhiteSpace(codeDisplay) ? null : codeDisplay.Trim();
        if (display is { Length: > CodeDisplayMaxLength })
            return Invalid("Subscription.Discount.InvalidCodeDisplay", "The code display is too long.");

        return Result.Success(
            new SubscriptionDiscountTerms
            {
                Source = source,
                CodeDefinitionId = codeDefinitionId,
                PercentBasisPoints = percentBasisPoints,
                AmountCents = amountCents,
                Currency = currency?.Trim().ToUpperInvariant(),
                Duration = duration,
                Months = months,
                AppliesTo = appliesTo,
                PlanCode = string.IsNullOrWhiteSpace(planCode) ? null : planCode.Trim(),
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                StartsAtUtc = startsAtUtc,
                CodeDisplay = display,
            }
        );
    }

    private static Result<SubscriptionDiscountTerms> Invalid(string code, string message) =>
        Result.Failure<SubscriptionDiscountTerms>(new Error(code, message));
}
