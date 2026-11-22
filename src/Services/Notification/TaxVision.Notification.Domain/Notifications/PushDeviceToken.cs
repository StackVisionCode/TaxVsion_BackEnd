using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Notifications;

public enum PushPlatform
{
    Fcm,
    Apns,
    Web,
}

/// <summary>
/// Token de dispositivo (FCM/APNs) de un usuario, para el canal
/// <see cref="NotificationChannel.Push"/>. Un token es unico por tenant — si
/// llega un token ya registrado (reinstalación, u otro usuario en el mismo
/// dispositivo compartido), <see cref="Reactivate"/> reasigna el owner en vez
/// de crear una fila duplicada.
/// </summary>
public sealed class PushDeviceToken : TenantEntity
{
    private PushDeviceToken() { }

    [UserReference(UserReferenceKind.Credential, HandledBy = "UserLifecyclePushDeviceConsumer")]
    public Guid UserId { get; private set; }
    public PushPlatform Platform { get; private set; }
    public string Token { get; private set; } = default!;
    public string? DeviceId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime RegisteredAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>Sesión de Auth que hizo la última alta (claim <c>sid</c>). Null en filas viejas o sin claim.</summary>
    public Guid? SessionId { get; private set; }

    /// <summary>Última alta vista; se refresca como mucho cada 12 h (el alta se repite en cada entrada).</summary>
    public DateTime? LastSeenAtUtc { get; private set; }

    private static readonly TimeSpan LastSeenRefresh = TimeSpan.FromHours(12);

    public static Result<PushDeviceToken> Register(
        Guid tenantId,
        Guid userId,
        PushPlatform platform,
        string token,
        string? deviceId,
        Guid? sessionId = null,
        DateTime? nowUtc = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<PushDeviceToken>(new Error("PushDeviceToken.Tenant", "Tenant is required."));
        if (userId == Guid.Empty)
            return Result.Failure<PushDeviceToken>(new Error("PushDeviceToken.User", "User is required."));
        if (string.IsNullOrWhiteSpace(token))
            return Result.Failure<PushDeviceToken>(new Error("PushDeviceToken.Token", "Token is required."));

        var now = nowUtc ?? DateTime.UtcNow;
        var entity = new PushDeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Platform = platform,
            Token = token.Trim(),
            DeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim(),
            IsActive = true,
            RegisteredAtUtc = now,
            SessionId = sessionId,
            LastSeenAtUtc = now,
        };
        entity.SetTenant(tenantId);
        return Result.Success(entity);
    }

    /// <summary>La sesión nueva se queda el token: un fin de sesión viejo que llegue tarde ya no lo apaga.</summary>
    public void Reactivate(
        Guid userId,
        PushPlatform platform,
        string? deviceId,
        Guid? sessionId = null,
        DateTime? nowUtc = null
    )
    {
        UserId = userId;
        Platform = platform;
        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
        IsActive = true;
        RevokedAtUtc = null;
        SessionId = sessionId;
        var now = nowUtc ?? DateTime.UtcNow;
        if (LastSeenAtUtc is not { } seen || now - seen >= LastSeenRefresh)
            LastSeenAtUtc = now;
    }

    public void Revoke()
    {
        IsActive = false;
        RevokedAtUtc = DateTime.UtcNow;
    }
}
