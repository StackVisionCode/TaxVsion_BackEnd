using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;

/// <summary>
/// Resultado de registrar la atribución. El referido no recibe ningún beneficio: solo el referidor gana (premio
/// en % del plan, a su Wallet), así que aquí no viaja ningún término de descuento.
/// </summary>
public sealed record CreateReferralAttributionResult(
    Guid AttributionId,
    ReferralAttributionStatus Status,
    bool WasReplay,
    // Quién referió (para avisarle). Opcionales con default: los resultados guardados antes de existir no los traen.
    Guid ReferrerTenantId = default,
    Guid? ReferrerUserId = null,
    string? Channel = null
);
