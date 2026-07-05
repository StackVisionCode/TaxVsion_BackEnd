using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Mfa;

/// <summary>"Recordar este dispositivo": permite omitir MFA durante un periodo limitado.</summary>
public sealed class TrustedDevice : TenantEntity
{
    private TrustedDevice() { }

    public Guid UserId { get; private set; }
    public string DeviceTokenHash { get; private set; } = default!;
    public string? UserAgent { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;

    public static TrustedDevice Create(
        Guid tenantId,
        Guid userId,
        string deviceTokenHash,
        string? userAgent,
        TimeSpan validity
    )
    {
        var device = new TrustedDevice
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeviceTokenHash = deviceTokenHash,
            UserAgent = userAgent is { Length: > 512 } ? userAgent[..512] : userAgent,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(validity),
        };
        device.SetTenant(tenantId);
        return device;
    }

    public void Revoke() => RevokedAtUtc ??= DateTime.UtcNow;
}
