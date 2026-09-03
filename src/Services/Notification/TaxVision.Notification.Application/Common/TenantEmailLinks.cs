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

    /// <summary>
    /// Deep-link a la pestaña de documentos del portal (donde el cliente ve "Requested from you"
    /// y sube lo pedido): <c>{ClientBase}/client/documents</c>. Un correo que pide subir un
    /// documento debe llevar AQUÍ, no a la raíz del portal (donde el cliente no ve la solicitud).
    /// </summary>
    public static string ClientDocuments(string? tenantHost, PortalOptions portal) =>
        $"{ClientBase(tenantHost, portal)}/client/documents";

    /// <summary>
    /// Enlace público de firma bajo el subdominio de la oficina:
    /// <c>https://{host}/signature/public/{token}</c>. El firmante es anónimo y la página se
    /// sirve en la raíz del host de la oficina; sin host resuelto cae al base staff fijo.
    /// </summary>
    public static string SigningLink(string? tenantHost, PortalOptions portal, string token) =>
        $"{StaffBase(tenantHost, portal)}/signature/public/{token}";

    /// <summary>
    /// Link público de descarga (share-link de CloudStorage) bajo el subdominio de la oficina:
    /// <c>https://{host}/storage/public/{shareToken}?email={email}</c>. El <c>?email</c> lo verifica
    /// el endpoint público contra los recipients del link (visibility ExternalRecipients).
    /// </summary>
    public static string PublicShareDownloadLink(
        string? tenantHost,
        PortalOptions portal,
        string shareToken,
        string email
    ) => $"{StaffBase(tenantHost, portal)}/storage/public/{shareToken}?email={Uri.EscapeDataString(email)}";

    /// <summary>
    /// Página pública branded de un share-link bajo el subdominio de la oficina:
    /// <c>https://{host}/s/{token}?email={email}</c>. A diferencia de <see cref="PublicShareDownloadLink"/>
    /// (302 directo a la descarga), esta lleva al destinatario a la página con marca donde ve el
    /// archivo y descarga desde ahí. El <c>?email</c> lo verifica el backend (visibility ExternalRecipients).
    /// </summary>
    public static string PublicSharePageLink(
        string? tenantHost,
        PortalOptions portal,
        string shareToken,
        string email
    ) => $"{StaffBase(tenantHost, portal)}/s/{shareToken}?email={Uri.EscapeDataString(email)}";

    private static string NormalizePrefix(string? prefix)
    {
        var p = (prefix ?? string.Empty).Trim().TrimEnd('/');
        if (p.Length == 0)
            return string.Empty;
        return p.StartsWith('/') ? p : $"/{p}";
    }
}
