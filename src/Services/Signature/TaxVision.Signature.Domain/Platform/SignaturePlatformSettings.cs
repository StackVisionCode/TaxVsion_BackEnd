using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Domain.Platform;

/// <summary>
/// Valores de seguridad y producto de Signature que administra PlatformAdmin (fila única, sin tenant):
/// código de verificación del firmante, imagen de la firma y topes de cantidad que no vende el plan. Antes
/// eran constantes repartidas por el código.
/// </summary>
public sealed class SignaturePlatformSettings : BaseEntity
{
    public static readonly Guid SingletonId = new("7a2c9e4b-1f3d-4b8a-9c6e-2d5f8a1b3c70");

    private SignaturePlatformSettings() { }

    public int OtpLength { get; private set; }
    public int OtpLifetimeMinutes { get; private set; }
    public int OtpResendCooldownSeconds { get; private set; }

    /// <summary>Intentos fallidos (código o PIN) antes de bloquear al firmante.</summary>
    public int MaxVerificationAttempts { get; private set; }

    public int VerificationLockMinutes { get; private set; }
    public int MaxSignatureImageBytes { get; private set; }
    public int MaxProfilesPerPreparer { get; private set; }
    public int MaxRolesPerTemplate { get; private set; }
    public int MaxRemindersPerRequest { get; private set; }
    public int MaxFieldsPerSigner { get; private set; }

    /// <summary>Rango de la letra automática de iniciales, fechas y textos (75 % del alto de la caja, acotado).</summary>
    public int AutoFontMinPt { get; private set; }

    public int AutoFontMaxPt { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Los valores que tenía el código al pasar a BD (semilla y respaldo si falta la fila).</summary>
    public static SignaturePlatformSettings Defaults() =>
        new()
        {
            Id = SingletonId,
            OtpLength = 6,
            OtpLifetimeMinutes = 10,
            OtpResendCooldownSeconds = 30,
            MaxVerificationAttempts = 5,
            VerificationLockMinutes = 30,
            MaxSignatureImageBytes = 1_500_000,
            MaxProfilesPerPreparer = 10,
            MaxRolesPerTemplate = 20,
            MaxRemindersPerRequest = 20,
            MaxFieldsPerSigner = 200,
            AutoFontMinPt = 6,
            AutoFontMaxPt = 12,
            UpdatedByUserId = Guid.Empty,
            UpdatedAtUtc = DateTime.UnixEpoch,
        };

    public VerificationPolicy Verification =>
        new(
            OtpLength,
            TimeSpan.FromMinutes(OtpLifetimeMinutes),
            TimeSpan.FromSeconds(OtpResendCooldownSeconds),
            MaxVerificationAttempts,
            TimeSpan.FromMinutes(VerificationLockMinutes)
        );

    public Result Update(
        int otpLength,
        int otpLifetimeMinutes,
        int otpResendCooldownSeconds,
        int maxVerificationAttempts,
        int verificationLockMinutes,
        int maxSignatureImageBytes,
        int maxProfilesPerPreparer,
        int maxRolesPerTemplate,
        int maxRemindersPerRequest,
        int maxFieldsPerSigner,
        int autoFontMinPt,
        int autoFontMaxPt,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (
            otpLength is < 4 or > 10
            || otpLifetimeMinutes is < 1 or > 60
            || otpResendCooldownSeconds is < 10 or > 600
            || maxVerificationAttempts is < 3 or > 20
            || verificationLockMinutes is < 1 or > 1440
            || maxSignatureImageBytes is < 50_000 or > 5_000_000
            || maxProfilesPerPreparer is < 1 or > 50
            || maxRolesPerTemplate is < 1 or > 100
            || maxRemindersPerRequest is < 0 or > 100
            || maxFieldsPerSigner is < 1 or > 1000
            || autoFontMinPt < FieldTypography.MinFontSizePt
            || autoFontMaxPt > FieldTypography.MaxFontSizePt
            || autoFontMinPt > autoFontMaxPt
        )
            return Result.Failure(SignaturePlatformSettingsErrors.OutOfRange);

        OtpLength = otpLength;
        OtpLifetimeMinutes = otpLifetimeMinutes;
        OtpResendCooldownSeconds = otpResendCooldownSeconds;
        MaxVerificationAttempts = maxVerificationAttempts;
        VerificationLockMinutes = verificationLockMinutes;
        MaxSignatureImageBytes = maxSignatureImageBytes;
        MaxProfilesPerPreparer = maxProfilesPerPreparer;
        MaxRolesPerTemplate = maxRolesPerTemplate;
        MaxRemindersPerRequest = maxRemindersPerRequest;
        MaxFieldsPerSigner = maxFieldsPerSigner;
        AutoFontMinPt = autoFontMinPt;
        AutoFontMaxPt = autoFontMaxPt;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}

/// <summary>Reglas del código de verificación y del bloqueo del firmante (código o PIN).</summary>
public sealed record VerificationPolicy(
    int OtpLength,
    TimeSpan OtpLifetime,
    TimeSpan ResendCooldown,
    int MaxAttempts,
    TimeSpan LockDuration
);

public static class SignaturePlatformSettingsErrors
{
    public static readonly Error OutOfRange = new(
        "SignaturePlatformSettings.OutOfRange",
        "Code 4–10 digits, valid 1–60 min, resend 10–600 s, 3–20 attempts, lock 1–1440 min, signature image 50 KB–5 MB, "
            + "1–50 profiles, 1–100 roles, 0–100 reminders, 1–1000 fields per signer and automatic font 6–72 pt (min ≤ max)."
    );
}
