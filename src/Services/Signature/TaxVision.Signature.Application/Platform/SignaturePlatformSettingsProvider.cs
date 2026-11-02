using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Domain.Platform;

namespace TaxVision.Signature.Application.Platform;

public interface ISignaturePlatformSettingsRepository
{
    /// <summary>Sin seguimiento: para leer los valores vigentes.</summary>
    Task<SignaturePlatformSettings?> GetAsync(CancellationToken ct);

    /// <summary>Con seguimiento: para que PlatformAdmin los cambie.</summary>
    Task<SignaturePlatformSettings?> GetForUpdateAsync(CancellationToken ct);
}

/// <summary>Valores de plataforma vigentes (fila única en BD, caché por réplica de 60 s).</summary>
public interface ISignaturePlatformSettingsProvider
{
    Task<SignaturePlatformSettings> GetAsync(CancellationToken ct);

    void Invalidate();
}

public sealed class SignaturePlatformSettingsProvider(
    ISignaturePlatformSettingsRepository repository,
    SignaturePlatformSettingsCache cache
) : ISignaturePlatformSettingsProvider
{
    public async Task<SignaturePlatformSettings> GetAsync(CancellationToken ct)
    {
        if (cache.TryGet(DateTime.UtcNow, out var cached))
            return cached;

        // Sin fila (entorno sin migrar) se usan los mismos valores que siembra la migración.
        var settings = await repository.GetAsync(ct) ?? SignaturePlatformSettings.Defaults();
        cache.Set(settings, DateTime.UtcNow);
        return settings;
    }

    public void Invalidate() => cache.Invalidate();
}

public sealed class SignaturePlatformSettingsCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);

    private (SignaturePlatformSettings Settings, DateTime ExpiresAtUtc)? _entry;

    public bool TryGet(DateTime nowUtc, out SignaturePlatformSettings settings)
    {
        if (_entry is { } entry && entry.ExpiresAtUtc > nowUtc)
        {
            settings = entry.Settings;
            return true;
        }
        settings = default!;
        return false;
    }

    public void Set(SignaturePlatformSettings settings, DateTime nowUtc) => _entry = (settings, nowUtc + TimeToLive);

    public void Invalidate() => _entry = null;
}

// ------------------------------------------------------------------
// PlatformAdmin: ver y cambiar
// ------------------------------------------------------------------

public sealed record GetSignaturePlatformSettingsQuery;

public sealed record UpdateSignaturePlatformSettingsCommand(
    int OtpLength,
    int OtpLifetimeMinutes,
    int OtpResendCooldownSeconds,
    int MaxVerificationAttempts,
    int VerificationLockMinutes,
    int MaxSignatureImageBytes,
    int MaxProfilesPerPreparer,
    int MaxRolesPerTemplate,
    int MaxRemindersPerRequest,
    int MaxFieldsPerSigner,
    int AutoFontMinPt,
    int AutoFontMaxPt,
    Guid ActorUserId
);

public sealed record SignaturePlatformSettingsResponse(
    int OtpLength,
    int OtpLifetimeMinutes,
    int OtpResendCooldownSeconds,
    int MaxVerificationAttempts,
    int VerificationLockMinutes,
    int MaxSignatureImageBytes,
    int MaxProfilesPerPreparer,
    int MaxRolesPerTemplate,
    int MaxRemindersPerRequest,
    int MaxFieldsPerSigner,
    int AutoFontMinPt,
    int AutoFontMaxPt,
    DateTime UpdatedAtUtc
)
{
    public static SignaturePlatformSettingsResponse From(SignaturePlatformSettings settings) =>
        new(
            settings.OtpLength,
            settings.OtpLifetimeMinutes,
            settings.OtpResendCooldownSeconds,
            settings.MaxVerificationAttempts,
            settings.VerificationLockMinutes,
            settings.MaxSignatureImageBytes,
            settings.MaxProfilesPerPreparer,
            settings.MaxRolesPerTemplate,
            settings.MaxRemindersPerRequest,
            settings.MaxFieldsPerSigner,
            settings.AutoFontMinPt,
            settings.AutoFontMaxPt,
            settings.UpdatedAtUtc
        );
}

public static class SignaturePlatformSettingsHandler
{
    public static async Task<SignaturePlatformSettingsResponse> Handle(
        GetSignaturePlatformSettingsQuery query,
        ISignaturePlatformSettingsProvider provider,
        CancellationToken ct
    ) => SignaturePlatformSettingsResponse.From(await provider.GetAsync(ct));

    public static async Task<Result<SignaturePlatformSettingsResponse>> Handle(
        UpdateSignaturePlatformSettingsCommand command,
        ISignaturePlatformSettingsRepository repository,
        ISignaturePlatformSettingsProvider provider,
        IUnitOfWork unitOfWork,
        ILogger<SignaturePlatformSettings> logger,
        CancellationToken ct
    )
    {
        var settings = await repository.GetForUpdateAsync(ct);
        if (settings is null)
            return Result.Failure<SignaturePlatformSettingsResponse>(
                new Error("SignaturePlatformSettings.Missing", "Platform settings are not provisioned; apply the migrations.")
            );

        var before = SignaturePlatformSettingsResponse.From(settings);
        var updated = settings.Update(
            command.OtpLength,
            command.OtpLifetimeMinutes,
            command.OtpResendCooldownSeconds,
            command.MaxVerificationAttempts,
            command.VerificationLockMinutes,
            command.MaxSignatureImageBytes,
            command.MaxProfilesPerPreparer,
            command.MaxRolesPerTemplate,
            command.MaxRemindersPerRequest,
            command.MaxFieldsPerSigner,
            command.AutoFontMinPt,
            command.AutoFontMaxPt,
            command.ActorUserId,
            DateTime.UtcNow
        );
        if (updated.IsFailure)
            return Result.Failure<SignaturePlatformSettingsResponse>(updated.Error);

        await unitOfWork.SaveChangesAsync(ct);
        provider.Invalidate();

        var after = SignaturePlatformSettingsResponse.From(settings);
        // La fila guarda quién y cuándo; el log deja el antes/después para auditoría.
        logger.LogInformation(
            "Signature platform settings changed by {ActorUserId}: {Before} -> {After}",
            command.ActorUserId,
            before,
            after
        );
        return Result.Success(after);
    }
}
