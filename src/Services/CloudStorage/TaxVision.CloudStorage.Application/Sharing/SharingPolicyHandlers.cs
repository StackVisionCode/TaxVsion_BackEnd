using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Platform;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Application.Sharing;

// ------------------------------------------------------------------
// Reglas de compartir de la oficina (cloudstorage.settings.manage)
// ------------------------------------------------------------------

public sealed record GetSharingPolicyQuery(Guid TenantId);

public sealed record UpdateSharingPolicyCommand(
    Guid TenantId,
    bool AllowLinkOnlyExternalShares,
    bool RequirePasswordOnLinkShares,
    int DefaultLifetimeDays,
    int MaxLifetimeDays,
    int MinPasswordLength,
    int MaxPasswordAttempts,
    int? MaxDownloads
);

/// <summary>Lo que la oficina decide sobre los enlaces externos; <see cref="AllowPublicShareLinks"/> se cambia aparte.</summary>
public sealed record SharingPolicyResponse(
    bool AllowPublicShareLinks,
    bool AllowLinkOnlyExternalShares,
    bool RequirePasswordOnLinkShares,
    int DefaultLifetimeDays,
    int MaxLifetimeDays,
    int MinPasswordLength,
    int MaxPasswordAttempts,
    int? MaxDownloads
)
{
    public static SharingPolicyResponse From(TenantStorageLimit limit) =>
        new(
            limit.AllowPublicShareLinks,
            limit.AllowLinkOnlyExternalShares,
            limit.RequirePasswordOnLinkShares,
            limit.DefaultShareLifetimeDays,
            limit.MaxShareLifetimeDays,
            limit.MinSharePasswordLength,
            limit.MaxSharePasswordAttempts,
            limit.MaxShareDownloads
        );
}

public static class SharingPolicyHandler
{
    public static async Task<Result<SharingPolicyResponse>> Handle(
        GetSharingPolicyQuery query,
        IStorageLimitRepository limits,
        CancellationToken ct
    )
    {
        var limit = await limits.GetAsync(query.TenantId, ct);
        return limit is null
            ? Result.Failure<SharingPolicyResponse>(QuotaErrors.NotProvisioned)
            : Result.Success(SharingPolicyResponse.From(limit));
    }

    public static async Task<Result<SharingPolicyResponse>> Handle(
        UpdateSharingPolicyCommand command,
        IStorageLimitRepository limits,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var limit = await limits.GetAsync(command.TenantId, ct);
        if (limit is null)
            return Result.Failure<SharingPolicyResponse>(QuotaErrors.NotProvisioned);

        var set = limit.SetSharingPolicy(
            command.AllowLinkOnlyExternalShares,
            command.RequirePasswordOnLinkShares,
            command.DefaultLifetimeDays,
            command.MaxLifetimeDays,
            command.MinPasswordLength,
            command.MaxPasswordAttempts,
            command.MaxDownloads
        );
        if (set.IsFailure)
            return Result.Failure<SharingPolicyResponse>(set.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(SharingPolicyResponse.From(limit));
    }
}

// ------------------------------------------------------------------
// Contraseña de un enlace: verificación con bloqueo por intentos
// ------------------------------------------------------------------

public enum SharePasswordOutcome
{
    NotRequired,
    Missing,
    Accepted,
    Wrong,

    /// <summary>Demasiados intentos fallidos: el enlace no acepta contraseñas por un rato.</summary>
    Locked,
}

/// <summary>
/// Una sola puerta para la contraseña de un enlace (archivo o carpeta): tras los intentos que fija la
/// oficina, el enlace se bloquea los minutos que fija la plataforma. Muta los contadores del enlace; el
/// llamador guarda.
/// </summary>
public static class SharePasswordGuard
{
    public static async Task<SharePasswordOutcome> CheckAsync(
        ShareLink link,
        string? password,
        IShareLinkPasswordHasher hasher,
        IStorageLimitRepository limits,
        IStoragePlatformSettingsProvider platform,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (link.PasswordHash is not { } passwordHash)
            return SharePasswordOutcome.NotRequired;
        if (link.IsPasswordLocked(nowUtc))
            return SharePasswordOutcome.Locked;
        if (string.IsNullOrEmpty(password))
            return SharePasswordOutcome.Missing;

        if (hasher.Verify(password, passwordHash))
        {
            link.RegisterCorrectPassword();
            return SharePasswordOutcome.Accepted;
        }

        var maxAttempts =
            (await limits.GetAsync(link.TenantId, ct))?.MaxSharePasswordAttempts
            ?? TenantStorageLimit.DefaultMaxSharePasswordAttempts;
        var lockMinutes = (await platform.GetAsync(ct)).SharePasswordLockMinutes;
        link.RegisterWrongPassword(maxAttempts, TimeSpan.FromMinutes(lockMinutes), nowUtc);
        return link.IsPasswordLocked(nowUtc) ? SharePasswordOutcome.Locked : SharePasswordOutcome.Wrong;
    }
}
