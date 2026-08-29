namespace TaxVision.Signature.Api.Common;

/// <summary>
/// Red de confianza para ForwardedHeadersMiddleware. Solo los proxies/redes listados aquí pueden
/// inyectar el header de IP real (ver <see cref="RealIpHeaderName"/>) de forma confiable; cualquier
/// otro origen los ve ignorados por el propio middleware de ASP.NET. Vacío por defecto — no confía en
/// nada hasta que el deploy configure la red interna real (red Docker / rango de Cloudflare).
///
/// <para>
/// Sin esto, la firma pública registra la IP del socket (el contenedor/proxy) en el certificado y en
/// el particionado del rate limiter, no la IP real del firmante detrás de Cloudflare.
/// </para>
/// </summary>
public sealed class ReverseProxyTrustOptions
{
    public const string SectionName = "ReverseProxyTrust";

    /// <summary>IPs individuales de confianza (ej. la IP del contenedor del Gateway).</summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>Redes en notación CIDR de confianza (ej. la subred de la red Docker interna).</summary>
    public List<string> KnownNetworks { get; set; } = [];

    /// <summary>Header con la IP real del cliente. Cloudflare usa "CF-Connecting-IP" en vez de X-Forwarded-For.</summary>
    public string RealIpHeaderName { get; set; } = "CF-Connecting-IP";
}
