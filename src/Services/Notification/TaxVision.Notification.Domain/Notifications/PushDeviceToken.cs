using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Notifications;

public enum PushPlatform
{
    Fcm,
    Apns,
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

    public Guid UserId { get; private set; }
    public PushPlatform Platform { get; private set; }
    public string Token { get; private set; } = default!;
    public string? DeviceId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime RegisteredAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    public static Result<PushDeviceToken> Register(
        Guid tenantId,
        Guid userId,
        PushPlatform platform,
        string token,
        string? deviceId
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<PushDeviceToken>(new Error("PushDeviceToken.Tenant", "Tenant is required."));
        if (userId == Guid.Empty)
            return Result.Failure<PushDeviceToken>(new Error("PushDeviceToken.User", "User is required."));
        if (string.IsNullOrWhiteSpace(token))
            return Result.Failure<PushDeviceToken>(new Error("PushDeviceToken.Token", "Token is required."));

        var entity = new PushDeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Platform = platform,
            Token = token.Trim(),
            DeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim(),
            IsActive = true,
            RegisteredAtUtc = DateTime.UtcNow,
        };
        entity.SetTenant(tenantId);
        return Result.Success(entity);
    }

    public void Reactivate(Guid userId, PushPlatform platform, string? deviceId)
    {
        UserId = userId;
        Platform = platform;
        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
        IsActive = true;
        RevokedAtUtc = null;
    }

    public void Revoke()
    {
        IsActive = false;
        RevokedAtUtc = DateTime.UtcNow;
    }
}
