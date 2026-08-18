using Microsoft.Extensions.Options;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Traduce la ruta entrante a su <see cref="RequestCriticality"/> usando el <b>primer segmento</b>
/// del path, declarado en <c>LoadShedding:Criticality</c>.
///
/// <para>
/// Va por configuración y no por <c>RouteConfig.Metadata</c> de YARP a propósito (GW-14, §9.1):
/// <c>LoadSheddingMiddleware</c> se registra antes de <c>MapReverseProxy()</c>, así que leer los
/// metadatos de la ruta ahí dependería de si el <c>UseRouting</c> que inserta
/// <c>WebApplication</c> ya corrió — un detalle invisible al leer <c>Program.cs</c> y que puede
/// cambiar entre versiones del framework. Por prefijo es determinista, se revisa en un diff y se
/// testea sin levantar YARP. La cobertura no queda a la buena fe:
/// <c>LoadSheddingCriticalityCoverageTests</c> falla si una ruta de <c>ReverseProxy:Routes</c> no
/// está clasificada.
/// </para>
/// </summary>
public sealed class RequestCriticalityClassifier(IOptionsMonitor<LoadShedderOptions> options)
{
    public RequestCriticality Classify(PathString path)
    {
        var current = options.CurrentValue;
        var segment = FirstSegment(path);

        return segment is not null && current.Criticality.TryGetValue(segment, out var criticality)
            ? criticality
            : current.DefaultCriticality;
    }

    /// <summary>Primer segmento sin barras, en minúsculas. <c>null</c> para la raíz.</summary>
    public static string? FirstSegment(PathString path)
    {
        if (!path.HasValue)
            return null;

        var value = path.Value!.AsSpan().TrimStart('/');
        var end = value.IndexOf('/');
        var segment = end < 0 ? value : value[..end];

        return segment.IsEmpty ? null : segment.ToString().ToLowerInvariant();
    }
}
