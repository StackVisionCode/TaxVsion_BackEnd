using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Settings.Commands.ApplyPlanConstraints;

/// <summary>
/// Comando de plataforma: establece los techos de plan para un tenant específico.
/// Sólo lo despacha un PlatformAdmin autenticado con el claim
/// <c>signature.constraints.manage</c>.
/// </summary>
public sealed record ApplyPlanConstraintsCommand(
    Guid TenantId,
    Guid ChangedByUserId,
    long MaxAllowedPdfBytes,
    long MaxAllowedImageBytes,
    int MaxAllowedPages,
    int MinRetentionYears,
    bool PurgeAllowed,
    VerificationChannel AllowedChannels,
    int MaxTokenExpirationHours
);
