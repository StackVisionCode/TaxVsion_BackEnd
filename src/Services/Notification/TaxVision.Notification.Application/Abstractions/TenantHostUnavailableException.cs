namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Motivo por el que no se pudo resolver el subdominio de la oficina.</summary>
public enum TenantHostUnavailableReason
{
    Transient,
    NoActiveSubdomain,
}

/// <summary>
/// Links de cuenta (reset, invitación, confirmación, registro) sin subdominio resuelto: el correo no sale
/// y la política estándar de Wolverine reintenta. Nunca se cae a <c>PortalOptions.BaseUrl</c> (D-04).
/// </summary>
public sealed class TenantHostUnavailableException(Guid tenantId, TenantHostUnavailableReason reason)
    : Exception($"Tenant {tenantId:D} has no resolvable platform subdomain ({reason}).")
{
    public Guid TenantId { get; } = tenantId;
    public TenantHostUnavailableReason Reason { get; } = reason;
}
