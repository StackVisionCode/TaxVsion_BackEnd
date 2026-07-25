using BuildingBlocks.Messaging;
using Wolverine;

namespace BuildingBlocks.Tenancy;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — establece el tenant del scope de un consumer desde el
/// contrato validado del propio evento. Los productores no siempre copian <c>TenantId</c> al
/// metadata nativo de Wolverine (<see cref="Wolverine.Envelope.TenantId"/>), así que el payload es
/// la fuente explícita de verdad para esta fase — un <see cref="Guid.Empty"/> falla cerrado
/// (excepción, no silencioso) porque procesar un evento sin saber su tenant real bajo un DbContext
/// fail-closed produciría 0 filas en cualquier query subsecuente, un fallo mucho más difícil de
/// diagnosticar que una excepción explícita al recibir el mensaje.
/// Extraído de <c>TaxVision.Growth.Api.Common.GrowthTenantMessageMiddleware</c> (primer uso real,
/// sin cambios de comportamiento) a BuildingBlocks.Web para reusar en los demás servicios.
///
/// <para>
/// <b>Bug real encontrado en producción/dev (2026-07-22)</b>: este middleware llenaba
/// <see cref="TenantContext"/> desde el payload del evento, pero nunca estampaba
/// <see cref="IMessageBus.TenantId"/> — a diferencia de <see cref="LocalCommandTenantMiddleware"/>
/// (que sí lo hace, pero solo corre para el <see cref="Envelope"/> entrante, cuyo
/// <see cref="Envelope.TenantId"/> viene vacío cuando el publicador nunca estampó su propio
/// <c>bus.TenantId</c> — exactamente el caso de un seeder/hosted service en background, como
/// <c>ScribeSystemAssetSeeder</c>, que publica <c>SaveFileRequestedIntegrationEvent</c> con el
/// tenant puesto en el campo del evento pero sin haber corrido nunca bajo
/// <see cref="JwtTenantContextMiddleware"/> ni <see cref="LocalCommandTenantMiddleware"/>). El
/// resultado: <c>SaveFileFromSourceHandler</c> (consumer de ese evento) corría con
/// <see cref="TenantContext"/> correcto, pero al publicar <c>ScanFileCommand</c> anidado ese
/// mensaje salía igual sin tenant en su propio envelope — el mismo bug de PendingScan colgado que
/// ya se había arreglado para el caso "comando local publica otro comando local", pero no para
/// "consumer de integration event publica un comando local". Por eso este middleware ahora también
/// estampa <c>IMessageBus.TenantId</c> desde el campo explícito del evento, no solo desde
/// <see cref="Envelope.TenantId"/>.
/// </para>
/// </summary>
public static class IntegrationEventTenantMiddleware
{
    public static void Before(IIntegrationEvent message, TenantContext tenantContext, IMessageBus bus)
    {
        if (message.TenantId == Guid.Empty)
            throw new InvalidOperationException($"Message {message.GetType().Name} does not contain a valid TenantId.");

        tenantContext.SetTenant(message.TenantId);
        bus.TenantId = message.TenantId.ToString();
    }
}
