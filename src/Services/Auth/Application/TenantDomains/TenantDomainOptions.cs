namespace TaxVision.Auth.Application.TenantDomains;

/// <summary>
/// Dominio base de la plataforma para componer subdominios de oficina (ej. "taxproffice.com"
/// en oficina1.taxproffice.com). Ver Auth_y_CloudStorage_Plan_Completitud_v2.md §9-10.
/// </summary>
public sealed class TenantDomainOptions
{
    public const string SectionName = "TenantDomains";

    public string BaseDomain { get; set; } = "taxproffice.com";

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
    /// Hosts de la plataforma que no son de ninguna oficina (apex, www, app, client): ahí no se busca TenantDomain ni
    /// se audita, y el tenant de una request autenticada sale del JWT. Vacío = se derivan de BaseDomain.
    /// <c>api.*</c> no va acá: está sembrado como dominio del tenant Platform (login del PlatformAdmin).
    /// </summary>
    public string[] SystemHosts { get; set; } = [];

    public bool IsSystemHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var hosts =
            SystemHosts.Length > 0
                ? SystemHosts
                : [BaseDomain, $"www.{BaseDomain}", $"app.{BaseDomain}", $"client.{BaseDomain}"];
        return hosts.Any(systemHost =>
            string.Equals(systemHost.Trim(), host.Trim(), StringComparison.OrdinalIgnoreCase)
        );
    }
}
