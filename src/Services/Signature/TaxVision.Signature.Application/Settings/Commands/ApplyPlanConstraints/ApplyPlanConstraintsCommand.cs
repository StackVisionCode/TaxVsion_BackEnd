using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Settings.Commands.ApplyPlanConstraints;

/// <summary>
/// Comando de plataforma: la excepción de un tenant sobre lo que vende su plan. Cada valor nulo hereda
/// del plan. Sólo lo despacha un PlatformAdmin con <c>signature.constraints.manage</c>.
/// </summary>
public sealed record ApplyPlanConstraintsCommand(
    Guid TenantId,
    Guid ChangedByUserId,
    int? MaxDocumentsPerRequest,
    int? MaxSignersPerRequest,
    long? MaxAllowedPdfBytes,
    int? MaxAllowedPages,
    int? MinRetentionYears,
    bool? PurgeAllowed,
    VerificationChannel? AllowedChannels,
    int? MaxTokenExpirationHours
);
