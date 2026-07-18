using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TaxVision.Scribe.Infrastructure.Observability;

/// <summary>
/// Meter + ActivitySource propios del servicio — registrados en OTel vía
/// <c>AddTaxVisionOpenTelemetry</c> (el nombre coincide con el <c>serviceName</c> pasado en
/// Program.cs: "scribe-service"). Mismo patrón que <c>PostmasterMetrics</c>, extendido con tracing
/// manual (Fase 6, plan §36 ítem 3 — "cada render un span").
/// </summary>
public static class ScribeTelemetry
{
    private const string ServiceName = "scribe-service";

    private static readonly Meter Meter = new(ServiceName);
    public static readonly ActivitySource ActivitySource = new(ServiceName);

    private static readonly Counter<long> RenderRequestsCounter = Meter.CreateCounter<long>(
        "scribe_render_requests_total"
    );
    private static readonly Histogram<double> RenderDurationHistogram = Meter.CreateHistogram<double>(
        "scribe_render_duration_seconds"
    );

    public static void RecordRenderRequest(string templateKey, string scope, string cacheLayer, string tenant) =>
        RenderRequestsCounter.Add(
            1,
            new KeyValuePair<string, object?>("template_key", templateKey),
            new KeyValuePair<string, object?>("scope", scope),
            new KeyValuePair<string, object?>("cache_layer", cacheLayer),
            new KeyValuePair<string, object?>("tenant", tenant)
        );

    public static void RecordRenderDuration(string templateKey, double seconds) =>
        RenderDurationHistogram.Record(seconds, new KeyValuePair<string, object?>("template_key", templateKey));
}
