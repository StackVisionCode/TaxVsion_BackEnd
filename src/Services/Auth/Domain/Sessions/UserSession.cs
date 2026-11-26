using BuildingBlocks.Domain;
using TaxVision.Auth.Domain.RefreshTokens;

namespace TaxVision.Auth.Domain.Sessions;

public sealed class UserSession : TenantEntity
{
    private UserSession() { }

    [UserReference(
        UserReferenceKind.Credential,
        HandledBy = "OffboardUserHandler",
        Note = "Denylist y revocación al instante (SessionAccessCutoff)."
    )]
    public Guid UserId { get; private set; }
    public string? DeviceName { get; private set; }
    public string? UserAgent { get; private set; }
    public string? IpAddress { get; private set; }

    /// <summary>IP con la que se inició la sesión; <see cref="IpAddress"/> es la última vista.</summary>
    public string? StartIpAddress { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevokedReason { get; private set; }

    /// <summary>Quién la cerró desde la gestión del equipo; nulo si la cerró su dueño o el sistema.</summary>
    [UserReference(UserReferenceKind.Provenance, Note = "Auditoría: quién cerró la sesión.")]
    public Guid? RevokedByUserId { get; private set; }
    public SessionChannel Channel { get; private set; }

    /// <summary>Ubicación aproximada al iniciar la sesión; no cambia después.</summary>
    public SessionLocation? Location { get; private set; }

    public bool IsActive => RevokedAtUtc is null;

    /// <summary>Una sesión nueva en esa superficie le disputa la sesión única a esta (mismo canal).</summary>
    public bool CompetesWith(SessionSurface surface) => Channel == ChannelOf(surface);

    private static SessionChannel ChannelOf(SessionSurface surface) =>
        surface == SessionSurface.Mobile ? SessionChannel.Mobile : SessionChannel.Web;

    public static UserSession Start(
        Guid tenantId,
        Guid userId,
        string? deviceName,
        string? userAgent,
        string? ipAddress,
        SessionSurface surface = SessionSurface.Workspace,
        SessionLocation? location = null
    )
    {
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeviceName = Truncate(deviceName, 100),
            UserAgent = Truncate(userAgent, 512),
            IpAddress = Truncate(ipAddress, 45),
            StartIpAddress = Truncate(ipAddress, 45),
            CreatedAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow,
            Channel = ChannelOf(surface),
            Location = location,
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

    public void Revoke(string reason, Guid? revokedByUserId = null)
    {
        if (RevokedAtUtc is not null)
            return;

        RevokedAtUtc = DateTime.UtcNow;
        RevokedReason = reason;
        RevokedByUserId = revokedByUserId;
    }

    /// <summary>Anota quién pidió un cierre hecho en bloque; no cambia una sesión abierta ni un autor ya anotado.</summary>
    public void AttributeRevocationTo(Guid revokedByUserId)
    {
        if (RevokedAtUtc is not null && RevokedByUserId is null)
            RevokedByUserId = revokedByUserId;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
