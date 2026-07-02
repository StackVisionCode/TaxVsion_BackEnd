using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Sessions;

public sealed class UserSession : TenantEntity
{
    private UserSession() { }

    public Guid UserId { get; private set; }
    public string? DeviceName { get; private set; }
    public string? UserAgent { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevokedReason { get; private set; }

    public bool IsActive => RevokedAtUtc is null;

    public static UserSession Start(
        Guid tenantId,
        Guid userId,
        string? deviceName,
        string? userAgent,
        string? ipAddress)
    {
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeviceName = Truncate(deviceName, 100),
            UserAgent = Truncate(userAgent, 512),
            IpAddress = Truncate(ipAddress, 45),
            CreatedAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow
        };
        session.SetTenant(tenantId);
        return session;
    }

    public void Touch(string? ipAddress)
    {
        LastSeenAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(ipAddress))
            IpAddress = Truncate(ipAddress, 45);
    }

    public void Revoke(string reason)
    {
        if (RevokedAtUtc is not null)
            return;

        RevokedAtUtc = DateTime.UtcNow;
        RevokedReason = reason;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null
            ? null
            : value.Length <= maxLength ? value : value[..maxLength];
}
