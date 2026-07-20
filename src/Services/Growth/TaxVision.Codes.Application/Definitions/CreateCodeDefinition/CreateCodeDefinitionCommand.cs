using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Codes.Application.Definitions.CreateCodeDefinition;

public sealed record CreateCodeDefinitionCommand(
    Guid OwnerTenantId,
    CodeOwnerScope OwnerScope,
    Guid? TenantScopeId,
    string Name,
    CodeKind Kind,
    string CodeToken,
    CodeBenefitType BenefitType,
    int? PercentageBasisPoints,
    long? FixedAmountCents,
    string? FixedAmountCurrency,
    long? MinimumPurchaseAmountCents,
    string? MinimumPurchaseCurrency,
    bool AllowStacking,
    DateTime StartsAtUtc,
    DateTime? ExpiresAtUtc,
    long? MaxRedemptions,
    long? MaxRedemptionsPerTenant,
    long? MaxRedemptionsPerSubject,
    IReadOnlyCollection<CreateCodeScopeInput>? Scopes,
    Guid ActorUserId,
    string IdempotencyKey
)
{
    public override string ToString() =>
        $"{nameof(CreateCodeDefinitionCommand)} {{ OwnerTenantId = {OwnerTenantId}, "
        + $"Name = {Name}, CodeToken = <redacted>, BenefitType = {BenefitType} }}";
}
