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
    /// <summary>
    /// Loguea el fallo y sigue — para call-sites donde el flujo que dispara el recalculo
    /// (compra de seat, cambio de plan, jobs de expiracion) ya completo su propio efecto
    /// principal y no debe deshacerse ni reintentarse solo porque el recalculo fallo.
    /// </summary>
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
                    + "POST /admin/subscription/tenants/{{tenantId}}/recalculate-entitlements.",
                tenantId,
                result.Error.Code,
                result.Error.Message
            );
        }
    }

    /// <summary>
    /// Bug real de produccion: TenantCreatedConsumer llamaba a RecalculateEntitlementsSafelyAsync,
    /// que convertia un Result.Failure en un simple log de error — sin excepcion, Wolverine
    /// consideraba el TenantCreatedIntegrationEvent como procesado con exito (la suscripcion SI
    /// se creo) y nunca reintentaba ni mandaba el mensaje a la dead-letter queue. Un tenant podia
    /// quedar para siempre sin TenantEntitlementSnapshot — y por lo tanto sin fila de
    /// TenantStorageLimits en CloudStorage — sin ningun rastro accionable mas alla de un log que
    /// nadie estaba mirando. Este metodo throw-ea en vez de tragarse el fallo, para que el mismo
    /// RetryWithCooldown(1s/5s/15s) + dead-letter de Program.cs se aplique aca tambien — igual que
    /// SaveFileFromSourceHandler en CloudStorage. Reprocesar TenantCreatedIntegrationEvent es
    /// seguro (ver doc-comment de TenantCreatedConsumer): la creacion de suscripcion es idempotente
    /// y el recalculo de entitlements es un upsert.
    /// </summary>
    public static async Task RecalculateEntitlementsOrThrowAsync(
        this IMessageBus bus,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(tenantId), ct);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"RecalculateEntitlementsCommand failed for tenant {tenantId}: {result.Error.Code} - {result.Error.Message}."
            );
        }
    }
}
