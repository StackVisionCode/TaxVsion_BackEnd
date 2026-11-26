namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Para los procesos automáticos (jobs, reintentos, envíos agendados): ¿la oficina tiene este módulo?
/// Una oficina suspendida, vencida o sin el módulo no lo tiene. Sin proyección todavía, sí: no se corta
/// a nadie por consistencia eventual (misma regla que el gate de módulo).
/// </summary>
public static class TenantModuleAccess
{
    public static async Task<bool> HasModuleAsync(
        this ITenantEntitlementModulesReader reader,
        Guid tenantId,
        string module,
        CancellationToken ct = default
    )
    {
        var modules = await reader.GetEnabledModulesAsync(tenantId, ct);
        return modules is null || modules.Contains(module, StringComparer.OrdinalIgnoreCase);
    }
}
