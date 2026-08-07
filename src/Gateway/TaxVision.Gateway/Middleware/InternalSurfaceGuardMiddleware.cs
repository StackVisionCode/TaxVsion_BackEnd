using TaxVision.Gateway.Observability;

namespace TaxVision.Gateway.Middleware;

/// <summary>
/// Bloquea con 404 cualquier petición cuyo path contenga un segmento <c>internal</c>. Los 18
/// controllers M2M del sistema viven bajo ese segmento y se hablan contenedor→contenedor
/// (<c>http://customer-api:8080/...</c>), nunca a través del Gateway: acá no hay tráfico legítimo
/// que perder. Ver GW-01 del plan de remediación.
/// </summary>
public sealed class InternalSurfaceGuardMiddleware(RequestDelegate next, ILogger<InternalSurfaceGuardMiddleware> logger)
{
    private const string InternalSegment = "internal";

    public async Task InvokeAsync(HttpContext context)
    {
        if (HasInternalSegment(context.Request.Path))
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
    private static bool HasInternalSegment(PathString path)
    {
        if (!path.HasValue)
            return false;

        foreach (var segment in path.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Equals(InternalSegment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
