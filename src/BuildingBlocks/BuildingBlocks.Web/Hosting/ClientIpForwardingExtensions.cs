using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Web.Hosting;

/// <summary>
/// Resolución uniforme de la IP real del cliente para TODOS los microservicios y el Gateway. Reemplaza
/// las configuraciones ad-hoc por-servicio: un solo punto para no volver a guardar la IP de Docker en
/// certificados, audit logs, sesiones ni el particionado del rate limiter.
///
/// <para>
/// Uso: <c>builder.Services.AddTaxVisionClientIpForwarding(builder.Configuration);</c> y, como PRIMER
/// middleware tras <c>builder.Build()</c>, <c>app.UseTaxVisionClientIp();</c> — debe ir antes de todo
/// lo que lea <c>Connection.RemoteIpAddress</c> (rate limiting, auth, controllers).
/// </para>
/// </summary>
public static class ClientIpForwardingExtensions
{
    // Rangos privados/loopback que, en un despliegue Docker detrás de Caddy, cubren la cadena interna
    // (Caddy → Gateway → servicio). Se usan por default cuando el deploy no fija una subred exacta.
    private static readonly string[] PrivateNetworkCidrs =
    [
        "127.0.0.0/8", // loopback IPv4
        "10.0.0.0/8", // RFC1918
        "172.16.0.0/12", // RFC1918 (rango típico de Docker)
        "192.168.0.0/16", // RFC1918
        "::1/128", // loopback IPv6
        "fc00::/7", // IPv6 ULA
    ];

    public static IServiceCollection AddTaxVisionClientIpForwarding(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var options =
            configuration.GetSection(ReverseProxyTrustOptions.SectionName).Get<ReverseProxyTrustOptions>()
            ?? new ReverseProxyTrustOptions();

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            // Cloudflare pone la IP real del cliente en CF-Connecting-IP (un solo valor, no spoofeable
            // desde internet: el edge sobrescribe cualquier CF-Connecting-IP entrante).
            if (!string.IsNullOrWhiteSpace(options.RealIpHeaderName))
                forwarded.ForwardedForHeaderName = options.RealIpHeaderName;

            // La confianza la determinan KnownProxies/KnownNetworks, no un límite de saltos. Con
            // CF-Connecting-IP (valor único) esto es seguro y evita perder la IP por cadenas largas.
            forwarded.ForwardLimit = null;

            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();

            foreach (var proxy in options.KnownProxies)
            {
                if (IPAddress.TryParse(proxy, out var ip))
                    forwarded.KnownProxies.Add(ip);
            }
            foreach (var network in options.KnownNetworks)
            {
                if (System.Net.IPNetwork.TryParse(network, out var net))
                    forwarded.KnownIPNetworks.Add(net);
            }

            var configuredExplicitly = forwarded.KnownProxies.Count > 0 || forwarded.KnownIPNetworks.Count > 0;
            if (!configuredExplicitly && options.TrustPrivateNetworksByDefault)
            {
                foreach (var cidr in PrivateNetworkCidrs)
                {
                    if (System.Net.IPNetwork.TryParse(cidr, out var net))
                        forwarded.KnownIPNetworks.Add(net);
                }
            }
        });

        return services;
    }

    /// <summary>Aplica el <c>ForwardedHeadersMiddleware</c> configurado. Debe ser el primer middleware.</summary>
    public static IApplicationBuilder UseTaxVisionClientIp(this IApplicationBuilder app) => app.UseForwardedHeaders();
}
