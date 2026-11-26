using Yarp.ReverseProxy.Transforms;

namespace TaxVision.Gateway.Middleware;

/// <summary>
/// El Gateway ya resolvió la IP real (UseForwardedHeaders consume CF-Connecting-IP), pero ese header queda
/// vacío hacia el backend: se re-inyecta para que cada servicio la lea igual que el Gateway. El valor que
/// haya mandado el cliente se descarta.
/// </summary>
public static class ClientIpProxyTransform
{
    private const string HeaderName = "CF-Connecting-IP";

    public static ValueTask Apply(RequestTransformContext transform)
    {
        var ip = transform.HttpContext.Connection.RemoteIpAddress;
        if (ip is not null)
        {
            if (ip.IsIPv4MappedToIPv6)
                ip = ip.MapToIPv4();
            transform.ProxyRequest.Headers.Remove(HeaderName);
            transform.ProxyRequest.Headers.TryAddWithoutValidation(HeaderName, ip.ToString());
        }
        return ValueTask.CompletedTask;
    }
}
