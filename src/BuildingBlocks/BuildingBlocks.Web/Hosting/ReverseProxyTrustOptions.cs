namespace BuildingBlocks.Web.Hosting;

/// <summary>
/// Red de confianza para el <c>ForwardedHeadersMiddleware</c> compartido de TaxVision. Detrás de
/// Cloudflare → Caddy → Gateway (YARP) → microservicio, la IP real del cliente NO es la del socket
/// (que es la del contenedor Gateway/Caddy en la red Docker) sino la que viaja en
/// <see cref="RealIpHeaderName"/> (Cloudflare usa <c>CF-Connecting-IP</c>).
///
/// <para>
/// ASP.NET solo aplica el header reenviado si la conexión inmediata viene de un proxy/red de
/// confianza. Por eso, si <see cref="KnownProxies"/> y <see cref="KnownNetworks"/> están vacíos y
/// <see cref="TrustPrivateNetworksByDefault"/> es <c>true</c>, se confía por default en las redes
/// privadas (Docker/RFC1918 + loopback) — seguro porque los servicios internos no se publican, el
/// único ingress es Caddy detrás de Cloudflare. El deploy puede restringirlo a la subred exacta.
/// </para>
/// </summary>
public sealed class ReverseProxyTrustOptions
{
    public const string SectionName = "ReverseProxyTrust";

    /// <summary>IPs individuales de confianza (ej. la IP del contenedor Gateway/Caddy).</summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>Redes CIDR de confianza (ej. la subred de la red Docker interna del compose).</summary>
    public List<string> KnownNetworks { get; set; } = [];

    /// <summary>Header con la IP real del cliente. Cloudflare usa "CF-Connecting-IP".</summary>
    public string RealIpHeaderName { get; set; } = "CF-Connecting-IP";

    /// <summary>
    /// Si no se configuró ningún proxy/red explícito, confiar en las redes privadas (Docker) por
    /// default. <c>false</c> = solo lo explícitamente listado (más estricto).
    /// </summary>
    public bool TrustPrivateNetworksByDefault { get; set; } = true;
}
