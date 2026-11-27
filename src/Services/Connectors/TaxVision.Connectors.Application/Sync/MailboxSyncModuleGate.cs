using BuildingBlocks.RateLimiting;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Una oficina sin el módulo de correo (suspendida, vencida o sin plan) no sincroniza sus buzones.
/// Los tokens y los watches se siguen renovando para que al reactivar no haya que reconectar.
/// </summary>
internal static class MailboxSyncModuleGate
{
    // Módulo comercial de connectors.* en PermissionModuleMap.
    private const string EmailModule = "email";

    public static async Task<bool> IsPausedAsync(
        ITenantEntitlementModulesReader modules,
        Guid tenantId,
        CancellationToken ct
    ) => !await modules.HasModuleAsync(tenantId, EmailModule, ct);
}
