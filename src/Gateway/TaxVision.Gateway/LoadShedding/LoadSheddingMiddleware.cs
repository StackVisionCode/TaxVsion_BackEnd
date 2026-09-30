using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using TaxVision.Gateway.Observability;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Capa 1 (load shedder global de flota). Mide la latencia del servidor — desde que entra el request
/// hasta que arranca la respuesta, round-trip al cluster YARP incluido — y la tasa de 5xx en
/// <see cref="RequestOutcomeWindow"/>; cuando <see cref="ILoadShedder"/> devuelve un descarte, responde
/// 503. Se mide hasta el arranque y no hasta el último byte porque lo que viene después (un export o un
/// archivo bajando) lo marca el ancho de banda del cliente, no la carga del servidor.
///
/// <para>
/// Nunca se cuentan ni se sheddean: <c>/health/*</c>, los upgrades WebSocket, las descargas ZIP
/// streameadas y los <see cref="LoadShedderOptions.PassThroughPathPrefixes"/> (Socket.IO completo, con
/// su long-polling) — su duración es la vida de una conexión, no trabajo del servidor, y envenenaba el
/// p99 disparando shedding sobre toda la flota. Los requests con un cuerpo grande se sheddean pero no
/// se miden. Debe ir después de <c>UseAuthentication()</c> para leer <c>tenant_id</c> del JWT validado.
/// </para>
/// </summary>
public sealed class LoadSheddingMiddleware(
    RequestDelegate next,
    ILoadShedder shedder,
    RequestOutcomeWindow window,
    TenantConsumptionTracker tenantTracker,
    IOptionsMonitor<LoadShedderOptions> options
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var current = options.CurrentValue;
        if (IsPassThrough(context, current))
        {
            await next(context);
            return;
        }

        var tenantKey =
            context.User.FindFirst("tenant_id")?.Value
            ?? TenantConsumptionTracker.AnonymousKeyFor(context.Connection.RemoteIpAddress);
        tenantTracker.RecordRequest(tenantKey);

        var verdict = shedder.Evaluate(tenantKey, context.Request.Path, context.RequestAborted.IsCancellationRequested);
        if (verdict != SheddingVerdict.Allowed)
        {
            await RejectAsync(context, tenantKey, verdict);
            return;
        }

        if (context.Request.ContentLength > current.UnmeasuredRequestBodyBytes)
        {
            await next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        double? responseStartedAtMs = null;
        context.Response.OnStarting(() =>
        {
            responseStartedAtMs = stopwatch.Elapsed.TotalMilliseconds;
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        finally
        {
            // Sin cuerpo escrito, los headers salen después de este middleware: ahí el total es la
            // latencia del servidor.
            window.Record(responseStartedAtMs ?? stopwatch.Elapsed.TotalMilliseconds, context.Response.StatusCode);
        }
    }

    private static bool IsPassThrough(HttpContext context, LoadShedderOptions current)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/health") || IsWebSocketUpgrade(context) || IsZipStreamingDownload(path))
            return true;

        foreach (var prefix in current.PassThroughPathPrefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                continue;

            var normalized = new PathString(prefix.StartsWith('/') ? prefix : "/" + prefix);
            if (path.StartsWithSegments(normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // No se apoya solo en IsWebSocketRequest: IHttpWebSocketFeature lo agrega UseWebSockets(), que va
    // después en el pipeline, así que aquí daba siempre false y los sockets entraban en la ventana. El
    // header Upgrade (HTTP/1.1) y el CONNECT extendido (HTTP/2) ya vienen de Kestrel.
    private static bool IsWebSocketUpgrade(HttpContext context) =>
        context.WebSockets.IsWebSocketRequest
        || context.Features.Get<IHttpExtendedConnectFeature>()?.IsExtendedConnect == true
        || context.Request.Headers.Upgrade.Any(value =>
            value?.Contains("websocket", StringComparison.OrdinalIgnoreCase) == true
        );

    // Los dos endpoints que streamean un .zip por el Gateway terminan en "/zip" bajo "/storage"
    // (/storage/files/zip y /storage/public/{token}/zip). Todo lo demás de /storage es JSON o un 302.
    private static bool IsZipStreamingDownload(PathString path) =>
        path.StartsWithSegments("/storage") && path.Value?.EndsWith("/zip", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Los anónimos se reparten por IP, pero la IP no va como etiqueta: cada IP nueva sería una serie
    /// nueva en Prometheus justo en plena sobrecarga.
    /// </summary>
    internal static string MetricTenantKey(string tenantKey) =>
        tenantKey.StartsWith(TenantConsumptionTracker.AnonymousKeyPrefix, StringComparison.Ordinal)
            ? "anonymous"
            : tenantKey;

    private async Task RejectAsync(HttpContext context, string tenantKey, SheddingVerdict verdict)
    {
        GatewayMetrics.RequestsShed.Add(
            1,
            new KeyValuePair<string, object?>("tenant_key", MetricTenantKey(tenantKey)),
            new KeyValuePair<string, object?>("reason", verdict.ToString())
        );

        // El cliente ya cortó: escribir el 503 lanzaría por la conexión muerta y ensuciaría los logs
        // con un error que no lo es. Solo se contabiliza y se corta.
        if (verdict == SheddingVerdict.Abandoned)
            return;

        var retryAfter = shedder.RetryAfterSeconds;
        context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context
            .Response.WriteAsJsonAsync(
                new
                {
                    Code = "LoadShedding.Active",
                    Message = $"We're receiving an unusually high number of requests. Please try again in {FormatSeconds(retryAfter)}.",
                    RetryAfterSeconds = retryAfter,
                },
                context.RequestAborted
            )
            .ConfigureAwait(false);
    }

    private static string FormatSeconds(int seconds) => seconds == 1 ? "1 second" : $"{seconds} seconds";
}
