using TaxVision.Gateway.Observability;

namespace TaxVision.Gateway.Middleware;

/// <summary>
/// Bloquea con 404 la superficie que solo se habla contenedor→contenedor, nunca a través del Gateway:
/// <list type="bullet">
/// <item>Cualquier path con un segmento <c>internal</c>: los controllers M2M del sistema viven ahí
/// (<c>http://customer-api:8080/internal/...</c>).</item>
/// <item>El grant de tokens de servicio, <c>/auth/service-token</c>: cada servicio lo pide directo a
/// <c>http://auth-api:8080</c>. Es anónimo por diseño, así que expuesto dejaría probar secretos de
/// clientes M2M desde Internet.</item>
/// </list>
/// Acá no hay tráfico legítimo que perder. Ver GW-01 del plan de remediación.
/// </summary>
public sealed class InternalSurfaceGuardMiddleware(RequestDelegate next, ILogger<InternalSurfaceGuardMiddleware> logger)
{
    private const string InternalSegment = "internal";

    /// <summary>Endpoints exactos solo de servicio, por segmentos (sin importar mayúsculas ni barras extra).</summary>
    private static readonly string[][] ServiceOnlyEndpoints =
    [
        ["auth", "service-token"],
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsServiceOnly(context.Request.Path))
        {
            // Warning, no Information: una petición acá solo puede venir de fuera de la red Docker.
            logger.LogWarning(
                "Sondeo de superficie interna bloqueado: {Method} {Path} desde {RemoteIp}",
                context.Request.Method,
                context.Request.Path.Value,
                context.Connection.RemoteIpAddress
            );
            GatewayMetrics.InternalSurfaceProbesBlocked.Add(1);

            // 404 y no 403: OWASP lo prefiere para no confirmar que el recurso existe. Un 403
            // regalaría el mapa de la superficie interna a quien la esté sondeando.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Compara segmento a segmento, no <c>Contains("/internal/")</c>: así también atrapa los paths
    /// que terminan en el segmento (un <c>GET /internal</c> a secas) y no se deja engañar por un
    /// recurso que solo empiece por esas letras (<c>/documents/internal-audit</c> sí pasa).
    /// </summary>
    private static bool IsServiceOnly(PathString path)
    {
        if (!path.HasValue)
            return false;

        var segments = path.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment.Equals(InternalSegment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var endpoint in ServiceOnlyEndpoints)
        {
            if (segments.AsSpan().SequenceEqual(endpoint, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
