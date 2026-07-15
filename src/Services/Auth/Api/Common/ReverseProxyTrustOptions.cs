namespace TaxVision.Auth.Api.Common;

/// <summary>
/// Red de confianza para ForwardedHeadersMiddleware (Fase A3). Solo proxies/redes
/// listados aquí pueden inyectar X-Forwarded-Proto/Host o el header de IP real
/// (ver RealIpHeaderName) de forma confiable; cualquier otro origen los ve ignorados
/// por el propio middleware de ASP.NET. Vacío por defecto — no confía en nada hasta
/// que el deploy configure la red interna real (docker network / rango de Cloudflare).
/// </summary>
public sealed class ReverseProxyTrustOptions
{
    public const string SectionName = "ReverseProxyTrust";

    /// <summary>IPs individuales de confianza (ej. la IP del contenedor del Gateway).</summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>Redes en notación CIDR de confianza (ej. la subred de la red Docker interna).</summary>
    public List<string> KnownNetworks { get; set; } = [];

    /// <summary>Header con la IP real del cliente. Cloudflare usa "CF-Connecting-IP" en vez de X-Forwarded-For.</summary>
    public string RealIpHeaderName { get; set; } = "X-Forwarded-For";
}
