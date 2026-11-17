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
    string IdempotencyKey,
    // Día elegido (P30): vence al final de ese día en la zona comercial. Gana sobre ExpiresAtUtc.
    DateOnly? ExpiresOn = null,
    // Cuánto dura el descuento y dónde se usa; por defecto, como los códigos de antes (primer cobro, al registrarse).
    CodeDurationKind Duration = CodeDurationKind.Once,
    int? DurationMonths = null,
    CodeUsageContext UsableOn = CodeUsageContext.Signup,
    // Solo para esta oficina (alcance Include de tipo Tenant).
    Guid? RestrictToTenantId = null
)
{
    public override string ToString() =>
        $"{nameof(CreateCodeDefinitionCommand)} {{ OwnerTenantId = {OwnerTenantId}, "
        + $"Name = {Name}, CodeToken = <redacted>, BenefitType = {BenefitType} }}";
}
