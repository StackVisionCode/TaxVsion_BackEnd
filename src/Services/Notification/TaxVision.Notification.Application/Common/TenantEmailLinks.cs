namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Base de los links de los correos, per-tenant. Cada oficina vive en su subdominio de plataforma
/// (ej. manfer.taxproffice.com): el CRM (staff) se sirve en la raíz y el portal del cliente bajo
/// <c>ClientPathPrefix</c> (ej. /portal). Si no se pudo resolver el host del tenant se cae al base
/// fijo configurado en <c>PortalOptions</c> — degradado, pero el correo igual sale.
/// </summary>
public static class TenantEmailLinks
{
    /// <summary>Base para links de STAFF (CRM): <c>https://{host}</c>.</summary>
    public static string StaffBase(string? tenantHost, PortalOptions portal) =>
        string.IsNullOrWhiteSpace(tenantHost) ? portal.BaseUrl.TrimEnd('/') : $"https://{tenantHost}";

    /// <summary>Base para links de CLIENTE (portal): <c>https://{host}{ClientPathPrefix}</c>.</summary>
    public static string ClientBase(string? tenantHost, PortalOptions portal) =>
        string.IsNullOrWhiteSpace(tenantHost)
            ? portal.ClientBaseUrl.TrimEnd('/')
            : $"https://{tenantHost}{NormalizePrefix(portal.ClientPathPrefix)}";

    private static string NormalizePrefix(string? prefix)
    {
        var p = (prefix ?? string.Empty).Trim().TrimEnd('/');
        if (p.Length == 0)
            return string.Empty;
        return p.StartsWith('/') ? p : $"/{p}";
    }
}
