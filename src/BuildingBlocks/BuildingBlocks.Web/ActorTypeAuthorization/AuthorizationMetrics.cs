using System.Diagnostics.Metrics;

namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// RBAC Fase 10 (RBAC_Hardening_Plan.md) — observabilidad del pipeline de autorización.
/// Meter compartido por los 14 servicios (registrado incondicionalmente en
/// <c>OpenTelemetryRegistration.AddTaxVisionOpenTelemetry</c>, ya que Layer 1/2 corren siempre).
/// No se etiqueta "service" acá — ya lo aporta el resource attribute <c>service.name</c> que cada
/// servicio setea vía <c>ConfigureResource(...AddService(serviceName))</c>. Nunca agregar tenantId,
/// userId ni ningún identificador personal como tag (cardinalidad + dato sensible).
/// </summary>
public sealed class AuthorizationMetrics : IDisposable
{
    public const string MeterName = "TaxVision.Authorization";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<int> _decisions;

    public AuthorizationMetrics()
    {
        _decisions = _meter.CreateCounter<int>(
            "authz.decision",
            description: "Authorization decisions by layer and result"
        );
    }

    /// <param name="layer">"1" (HasPermission), "2" (AllowActorTypes) o "3b" (resource ownership).</param>
    public void RecordDecision(bool allowed, string layer) =>
        _decisions.Add(
            1,
            new KeyValuePair<string, object?>("result", allowed ? "allow" : "deny"),
            new KeyValuePair<string, object?>("layer", layer)
        );

    public void Dispose() => _meter.Dispose();
}
