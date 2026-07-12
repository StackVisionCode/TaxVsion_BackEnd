using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Settings.Commands.UpdateSettings;

public sealed record UpdateSignatureSettingsCommand(
    Guid TenantId,
    Guid ChangedByUserId,
    VerificationChannel AllowedChannels,
    VerificationChannel DefaultChannel,
    int DefaultTokenExpirationHours,
    bool RemindersEnabledByDefault,
    bool GenerateCertificateByDefault,
    long MaxPdfBytes,
    long MaxImageBytes,
    int MaxPagesPerDocument,
    int RetentionYears,
    bool AllowPurge
);
