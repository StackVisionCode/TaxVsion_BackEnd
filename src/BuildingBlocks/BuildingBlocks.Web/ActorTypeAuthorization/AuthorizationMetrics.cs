using System.Diagnostics.Metrics;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

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
    private readonly Counter<int> _sessionDenylistUnavailable;

    public AuthorizationMetrics()
    {
        _decisions = _meter.CreateCounter<int>(
            "authz.decision",
            description: "Authorization decisions by layer and result"
        );
        _sessionDenylistUnavailable = _meter.CreateCounter<int>(
            "authz.session_denylist_unavailable",
            description: "Session denylist checks that could not be resolved (store unavailable)"
        );
    }

    /// <param name="layer">"1" (HasPermission), "2" (AllowActorTypes) o "3b" (resource ownership).</param>
    public void RecordDecision(bool allowed, string layer) =>
        _decisions.Add(
            1,
            new KeyValuePair<string, object?>("result", allowed ? "allow" : "deny"),
            new KeyValuePair<string, object?>("layer", layer)
        );

    /// <summary>
    /// H-06 — el store de la denylist (Redis) no respondió. Antes el fail-open era invisible: no
    /// había forma de saber cuántas revocaciones se estaban ignorando durante un incidente.
    /// </summary>
    /// <param name="outcome">"fail_open" (se dejó pasar) o "fail_closed" (se respondió 503).</param>
    public void RecordSessionDenylistUnavailable(string outcome) =>
        _sessionDenylistUnavailable.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void Dispose() => _meter.Dispose();
}
