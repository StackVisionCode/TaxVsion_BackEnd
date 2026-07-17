using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;

/// <summary>
/// Unico punto de disparo de <see cref="RecalculateEntitlementsCommand"/> — reemplaza el
/// "await bus.InvokeAsync&lt;Result&gt;(new RecalculateEntitlementsCommand(...), ct);" repetido
/// en ~20 call-sites (TenantCreatedConsumer, jobs de expiracion, handlers de plan/seat/add-on),
/// que hasta ahora ignoraban el Result devuelto: si el recalculo fallaba, el tenant quedaba con
/// una suscripcion valida pero sin TenantEntitlementSnapshot — y por lo tanto sin que Auth/
/// CloudStorage/Communication se enteraran nunca — sin ningun log de error.
/// </summary>
public static class RecalculateEntitlementsExtensions
{
    public static async Task RecalculateEntitlementsSafelyAsync(
        this IMessageBus bus,
        Guid tenantId,
        ILogger logger,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(tenantId), ct);
        if (result.IsFailure)
        {
            logger.LogError(
                "RecalculateEntitlementsCommand failed for tenant {TenantId}: {ErrorCode} - {ErrorMessage}. "
                    + "The tenant's entitlement snapshot is now stale or missing, so downstream services "
                    + "(Auth/CloudStorage/Communication) won't see the change. Retry via "
                    + "POST /admin/tenants/{{tenantId}}/recalculate-entitlements.",
                tenantId,
                result.Error.Code,
                result.Error.Message
            );
        }
    }
}
