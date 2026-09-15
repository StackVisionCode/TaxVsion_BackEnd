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

    /// <summary>
    /// Módulos comerciales habilitados por el plan del tenant (los <c>module.*</c> del entitlement
    /// snapshot). Default vacío: un servicio que todavía NO persiste módulos (no migró la columna)
    /// devuelve <c>[]</c> — no participa del gate de módulo salvo que además registre
    /// <c>ITenantModuleEntitlementsSource</c> (opt-in). Los servicios que sí los persisten sobreescriben
    /// esta propiedad.
    /// </summary>
    IReadOnlyList<string> EnabledModules => [];

    /// <summary>Aplica el nuevo estado solo si <paramref name="revisionNumber"/> no es más viejo que el actual.</summary>
    void ApplyIfNewer(string planCode, long revisionNumber);

    /// <summary>
    /// Igual que <see cref="ApplyIfNewer(string, long)"/> pero también aplica los módulos habilitados.
    /// Default interface method NO-breaking: ignora los módulos y delega en el overload de 2 args, así
    /// los servicios que aún no persisten módulos siguen compilando sin cambios (Open/Closed). Un
    /// servicio que persiste módulos sobreescribe este método.
    /// </summary>
    void ApplyIfNewer(string planCode, long revisionNumber, IReadOnlyList<string> enabledModules) =>
        ApplyIfNewer(planCode, revisionNumber);
}
