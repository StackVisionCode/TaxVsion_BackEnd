namespace BuildingBlocks.RateLimiting;

/// <summary>
/// RateLimit Fase 1 (extracción BuildingBlocks) — puerto marcador que cada servicio implementa
/// en su propia entidad de proyección local (p.ej. <c>TenantPlanCodeProjection</c> de Customer),
/// igual que <c>UserPermissionsProjection</c> ya se replica por servicio (RBAC Fase 7). La tabla
/// en sí NO se extrae — cada bounded context sigue siendo dueño de su propia persistencia.
/// </summary>
public interface ITenantPlanCodeProjection
{
    Guid TenantId { get; }
    string PlanCode { get; }
    long RevisionNumber { get; }

    /// <summary>Aplica el nuevo estado solo si <paramref name="revisionNumber"/> no es más viejo que el actual.</summary>
    void ApplyIfNewer(string planCode, long revisionNumber);
}
