using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Acceso a datos de los límites del plan contratado por el tenant
/// (usuarios máximos, invitaciones pendientes y módulos habilitados).
/// </summary>
public interface ITenantPlanLimitsStore
{
    Task<TenantPlanLimits?> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantPlanLimits limits, CancellationToken ct = default);
}
