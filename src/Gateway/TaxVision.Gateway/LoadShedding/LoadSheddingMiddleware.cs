using System.Diagnostics;
using TaxVision.Gateway.Observability;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Capa 1 (load shedder global de flota). Mide su propia latencia (incluye el round-trip completo al
/// cluster YARP de destino) y la tasa de 5xx en <see cref="RequestOutcomeWindow"/>; cuando
/// <see cref="ILoadShedder"/> devuelve un descarte, responde 503. Health checks (<c>/health/*</c>) y
/// upgrades WebSocket (long-lived, envenenarían el p99) nunca se cuentan ni se sheddean — se excluyen
/// antes de tocar cualquier estado. Debe ir después de
/// <c>UseAuthentication()</c>/<c>UseAuthorization()</c> para poder leer <c>tenant_id</c> del JWT ya
/// validado.
/// </summary>
public sealed class LoadSheddingMiddleware(
    RequestDelegate next,
    ILoadShedder shedder,
    RequestOutcomeWindow window,
    TenantConsumptionTracker tenantTracker
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        // Los upgrades WebSocket (Socket.IO de communication) son conexiones long-lived: su "duración"
        // es la vida del socket (segundos o minutos), no el trabajo de una request. Si se registran en
        // la ventana, envenenan el p99 (una conexión de 4 min entra como una latencia de 240.000 ms) y
        // disparan load shedding sobre toda la flota. No se cuentan ni se sheddean — como /health.
        if (context.WebSockets.IsWebSocketRequest)
        {
            await next(context);
            return;
        }

        var tenantKey = context.User.FindFirst("tenant_id")?.Value ?? TenantConsumptionTracker.AnonymousKey;
        tenantTracker.RecordRequest(tenantKey);

        var verdict = shedder.Evaluate(tenantKey, context.Request.Path, context.RequestAborted.IsCancellationRequested);
        if (verdict != SheddingVerdict.Allowed)
        {
            await RejectAsync(context, tenantKey, verdict);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            window.Record(stopwatch.Elapsed.TotalMilliseconds, context.Response.StatusCode);
        }
    }

    private async Task RejectAsync(HttpContext context, string tenantKey, SheddingVerdict verdict)
    {
        GatewayMetrics.RequestsShed.Add(
            1,
            new KeyValuePair<string, object?>("tenant_key", tenantKey),
            new KeyValuePair<string, object?>("reason", verdict.ToString())
        );

        // El cliente ya cortó: escribir el 503 lanzaría por la conexión muerta y ensuciaría los logs
        // con un error que no lo es. Solo se contabiliza y se corta.
        if (verdict == SheddingVerdict.Abandoned)
            return;

        context.Response.Headers["Retry-After"] = shedder.RetryAfterSeconds.ToString();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context
            .Response.WriteAsJsonAsync(
                new
                {
                    Code = "LoadShedding.Active",
                    Message = $"Fleet is overloaded. Retry after {shedder.RetryAfterSeconds} seconds.",
                },
                context.RequestAborted
            )
            .ConfigureAwait(false);
    }
}
