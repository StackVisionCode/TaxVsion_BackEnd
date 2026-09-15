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
    /// Encola el recálculo (no lo invoca inline): se PUBLICA para que corra DESPUÉS de que la
    /// transacción del handler commitee (outbox durable). Un <c>InvokeAsync</c> anidado corre en un
    /// scope/DbContext nuevo y NO ve los cambios aún no commiteados del handler (ej. el add-on recién
    /// comprado), así que dejaría el snapshot viejo hasta un recálculo posterior. Al publicarlo,
    /// Wolverine lo entrega tras el commit y le aplica sus reintentos si el recálculo falla — para
    /// call-sites cuyo efecto principal ya se completó y no debe deshacerse por un fallo del recálculo.
    /// </summary>
    public static async Task RecalculateEntitlementsSafelyAsync(
        this IMessageBus bus,
        Guid tenantId,
        ILogger logger,
        CancellationToken ct
    )
    {
        await bus.PublishAsync(new RecalculateEntitlementsCommand(tenantId));
        logger.LogDebug("Entitlement recalculation queued for tenant {TenantId}.", tenantId);
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
