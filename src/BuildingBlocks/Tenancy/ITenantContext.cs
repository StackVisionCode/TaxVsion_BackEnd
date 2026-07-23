namespace BuildingBlocks.Tenancy;

/// <summary>
/// Tenant context ámbito-scoped. Se lee desde JWT/header en las capas HTTP; los
/// consumers/background jobs lo llenan explícitamente desde el payload del evento.
/// Cuando <see cref="HasTenant"/> es <c>false</c>, no hay filtro tenant activo — los
/// DbContexts con <c>HasQueryFilter</c> deben degradar a "sin filtro" en ese caso.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    bool HasTenant { get; }

    /// <summary>
    /// RBAC Fase 5 — permite a background jobs/consumers en capa Infrastructure (sin
    /// dependencia a BuildingBlocks.Web/ASP.NET Core) sellar el tenant efectivo antes de
    /// una query cross-tenant por-item, sin necesitar el tipo concreto <c>TenantContext</c>.
    /// </summary>
    void SetTenant(Guid tenantId);
}
