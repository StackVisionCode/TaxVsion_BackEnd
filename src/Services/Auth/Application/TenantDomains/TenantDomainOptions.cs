using BuildingBlocks.Platform;

namespace TaxVision.Auth.Application.TenantDomains;

/// <summary>
/// Comportamiento de la resolución de oficinas por Host. El dominio de la plataforma NO vive acá:
/// sale de <see cref="IPlatformSettings"/> (Tenant), así que cambiarlo no exige tocar esta config.
/// Ver Auth_y_CloudStorage_Plan_Completitud_v2.md §9-10.
/// </summary>
public sealed class TenantDomainOptions
{
    public const string SectionName = "TenantDomains";

    /// <summary>
    /// Si es true, TenantHostResolutionMiddleware responde 404 cuando el Host de la request
    /// no resuelve a un tenant activo. Se desactiva en Development para no romper el
    /// login/Postman locales (que no tienen un subdominio real apuntando a localhost).
    /// </summary>
    public bool EnforceHostResolution { get; set; } = true;

    /// <summary>
    /// TTL de una reserva de subdominio (§11 del plan v2): tiempo que el slug queda
    /// bloqueado para un solo email mientras el registro termina de completarse.
    /// </summary>
    public int SubdomainReservationTtlMinutes { get; set; } = 15;

    /// <summary>
    /// Hosts extra que no son de ninguna oficina (en dev: localhost). Se suman a los de la plataforma.
    /// </summary>
    public string[] SystemHosts { get; set; } = [];

    /// <summary>
    /// Hosts donde no se busca oficina ni se audita, y el tenant de una request autenticada sale del
    /// JWT: el apex, www, app, client, admin y los <see cref="SystemHosts"/>. El host de la API no va
    /// acá: es el del tenant Platform (login del PlatformAdmin) y se resuelve.
    /// </summary>
    public bool IsSystemHost(string? host, PlatformSettingsSnapshot platform)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (
            SystemHosts.Any(systemHost =>
                string.Equals(systemHost.Trim(), host.Trim(), StringComparison.OrdinalIgnoreCase)
            )
        )
            return true;

        var normalized = PlatformSettingsSnapshot.NormalizeHost(host);
        return platform.IsSharedHost(normalized)
            && normalized != PlatformSettingsSnapshot.NormalizeHost(platform.ApiHost);
    }
}
