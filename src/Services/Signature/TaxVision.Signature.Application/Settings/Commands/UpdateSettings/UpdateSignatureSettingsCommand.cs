using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Settings.Commands.UpdateSettings;

public sealed record UpdateSignatureSettingsCommand(
    Guid TenantId,
    Guid ChangedByUserId,
    VerificationChannel AllowedChannels,
    VerificationChannel DefaultChannel,
    int DefaultTokenExpirationHours,
    bool RemindersEnabledByDefault,
    int DefaultReminderIntervalHours,
    bool GenerateCertificateByDefault,
    bool AllowEmployeeOwnSignature,
    long MaxPdfBytes,
    long MaxImageBytes,
    int MaxPagesPerDocument,
    int RetentionYears,
    bool AllowPurge
);
