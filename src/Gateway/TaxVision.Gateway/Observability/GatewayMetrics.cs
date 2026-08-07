using System.Diagnostics.Metrics;

namespace TaxVision.Gateway.Observability;

/// <summary>
/// Meter propio del servicio — registrado en OTel vía <c>AddTaxVisionOpenTelemetry</c> (el nombre
/// coincide con el <c>serviceName</c> pasado en Program.cs: "gateway"). Mismo patrón que
/// <c>ConnectorsMetrics</c>/<c>PostmasterMetrics</c>.
/// </summary>
public static class GatewayMetrics
{
    private static readonly Meter Meter = new("gateway");

    /// <summary>Fase 5 del plan de rate limiting — Capa 1 (load shedder de flota).</summary>
    public static readonly Counter<long> LoadSheddingActivated = Meter.CreateCounter<long>(
        "gateway_load_shedding_activated_total"
    );

    /// <summary>Requests rechazados con 503 por el load shedder. Tag "tenant_key".</summary>
    public static readonly Counter<long> RequestsShed = Meter.CreateCounter<long>("gateway_requests_shed_total");

    /// <summary>
    /// Peticiones a la superficie interna bloqueadas con 404 (GW-01). Cualquier valor &gt; 0 es un
    /// sondeo desde internet: el M2M legítimo va contenedor→contenedor y no pasa por acá.
    /// </summary>
    public static readonly Counter<long> InternalSurfaceProbesBlocked = Meter.CreateCounter<long>(
        "gateway_internal_surface_probes_blocked_total"
    );
}
