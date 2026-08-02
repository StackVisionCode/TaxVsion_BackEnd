using System.Diagnostics;
using TaxVision.Gateway.Observability;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Capa 1 (load shedder global de flota) — Fase 5 del plan de rate limiting. Mide su propia
/// latencia (incluye el round-trip completo al cluster YARP de destino) y la tasa de 5xx en
/// <see cref="RequestOutcomeWindow"/>; cuando <see cref="ILoadShedder"/> decide sobrecarga,
/// rechaza con 503 los requests de los tenants de mayor consumo actual. Health checks
/// (<c>/health/*</c>) nunca se cuentan ni se sheddean — se excluyen antes de tocar cualquier
/// estado. Debe ir después de <c>UseAuthentication()</c>/<c>UseAuthorization()</c> para poder leer
/// <c>tenant_id</c> del JWT ya validado.
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

        var tenantKey = context.User.FindFirst("tenant_id")?.Value ?? TenantConsumptionTracker.AnonymousKey;
        tenantTracker.RecordRequest(tenantKey);

        if (shedder.ShouldShed(tenantKey))
        {
            GatewayMetrics.RequestsShed.Add(1, new KeyValuePair<string, object?>("tenant_key", tenantKey));

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
}
