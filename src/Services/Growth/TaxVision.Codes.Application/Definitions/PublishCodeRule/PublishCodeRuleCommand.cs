using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Codes.Application.Definitions.PublishCodeRule;

/// <summary>Nueva versión de la regla; las cotizaciones y reservas ya hechas conservan la suya.</summary>
public sealed record PublishCodeRuleCommand(
    Guid OwnerTenantId,
    Guid CodeDefinitionId,
    CodeBenefitType BenefitType,
    int? PercentageBasisPoints,
    long? FixedAmountCents,
    string? FixedAmountCurrency,
    long? MinimumPurchaseAmountCents,
    string? MinimumPurchaseCurrency,
    bool AllowStacking,
    CodeDurationKind Duration,
    int? DurationMonths,
    Guid ActorUserId,
    string IdempotencyKey
);
