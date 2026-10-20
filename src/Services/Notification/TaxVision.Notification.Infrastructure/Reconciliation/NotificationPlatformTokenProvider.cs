using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;

namespace TaxVision.Notification.Infrastructure.Reconciliation;

/// <summary>Token de la PlatformTenant para el pull cross-tenant de la proyección de asignaciones.</summary>
internal sealed class NotificationPlatformTokenProvider(IServiceTokenAcquirer tokenAcquirer)
    : IPlatformServiceTokenProvider
{
    public Task<string?> GetPlatformTokenAsync(CancellationToken ct = default) =>
        tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
}
