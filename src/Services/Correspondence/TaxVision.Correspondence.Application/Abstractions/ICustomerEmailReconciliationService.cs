namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Fase 16, plan §32 R1 — corrige drift entre la proyección local <c>CustomerEmailAddresses</c> y
/// el estado real de Customer para UN tenant puntual (un email que cambió por un camino que de
/// alguna forma no generó/procesó limpio su evento, o una fila que quedó soft-deleted mientras el
/// customer real ya estaba activo de nuevo). Deliberadamente separado de
/// <see cref="ITenantCustomerBackfillService"/>: backfill corre UNA vez por tenant nuevo
/// (gateado por <see cref="ITenantBackfillStateRepository"/>), esto corre periódicamente sobre
/// tenants YA backfilleados — misma fuente (<see cref="ICorrespondenceCustomerClient"/>), lógica de
/// comparación distinta (crear vs. actualizar/reactivar). Ver <c>CustomerEmailReconciliationJob</c>
/// (Infrastructure) para el disparador periódico — esta interfaz es la lógica testeable, sin
/// depender de <c>BackgroundService</c>/DI de scope (guardrail SRP).
/// </summary>
public interface ICustomerEmailReconciliationService
{
    /// <summary>
    /// Pagina TODOS los customers activos del tenant en Customer y corrige cualquier drift
    /// encontrado. No lanza ante una falla de red/HTTP de una página — devuelve
    /// <see cref="CustomerEmailReconciliationResult.CompletedFully"/> en <c>false</c> y el caller
    /// decide cómo loguear/reintentar (mismo criterio que
    /// <c>ITenantCustomerBackfillService.EnsureBackfilledAsync</c>).
    /// </summary>
    Task<CustomerEmailReconciliationResult> ReconcileTenantAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Resultado de una corrida de reconciliación para un tenant. <see cref="CompletedFully"/> en
/// <c>false</c> significa que una página de <c>ListActiveCustomersAsync</c> falló a mitad de
/// camino — lo ya corregido en páginas previas queda persistido igual, la corrida siguiente
/// retoma desde la página 1 (no hay cursor: el volumen esperado por tenant no lo justifica, mismo
/// criterio que el backfill).
/// </summary>
public sealed record CustomerEmailReconciliationResult(int Created, int Updated, int Reactivated, bool CompletedFully)
{
    public int TotalFixed => Created + Updated + Reactivated;
}
