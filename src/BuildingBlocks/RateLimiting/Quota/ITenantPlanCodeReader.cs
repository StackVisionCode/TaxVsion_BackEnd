namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Puerto de solo lectura para "¿qué PlanCode tiene este tenant hoy?" — deliberadamente angosto
/// (mismo criterio que <c>IUserPermissionsProjectionReader</c>), sin acoplar
/// <see cref="IRateLimitQuotaResolver"/> a Subscription real. Cada servicio que active el
/// resolver (Fase 6) implementa este puerto sobre su propia proyección local, mantenida por un
/// consumer de <c>TenantEntitlementsChangedIntegrationEvent</c>.
/// </summary>
/// <remarks>
/// Registrado <c>TryAddSingleton</c> — una implementación con dependencias Scoped (DbContext) debe
/// envolver un <c>IServiceScopeFactory</c> por llamada, no inyectarlas directo (captive dependency).
/// Ver <c>Customer.Infrastructure.RateLimiting.ScopedTenantPlanCodeReader</c>.
/// </remarks>
public interface ITenantPlanCodeReader
{
    /// <summary>Devuelve el PlanCode del tenant, o null si no se conoce (tenant desconocido o dato no disponible — el resolver trata null como fail-open).</summary>
    Task<string?> GetPlanCodeAsync(Guid tenantId, CancellationToken ct = default);
}
