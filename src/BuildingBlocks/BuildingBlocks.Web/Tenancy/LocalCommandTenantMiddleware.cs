using System.Collections.Concurrent;
using System.Reflection;
using Wolverine;

namespace BuildingBlocks.Tenancy;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — restaura el tenant dentro del DI scope que Wolverine crea
/// para correr un message handler. Wolverine le da a todo mensaje manejado (incluye comandos locales
/// invocados in-process vía <c>bus.InvokeAsync</c>) un scope nuevo, desconectado del scope de la
/// request HTTP donde <see cref="JwtTenantContextMiddleware"/> ya resolvió el tenant desde el JWT —
/// así que <see cref="TenantContext"/> arranca vacío de nuevo para los handlers locales, disparando
/// los chequeos fail-closed (HasQueryFilter de EF) antes de que corra ninguna lógica de dominio.
/// Extraído de <c>TaxVision.Growth.Api.Common.GrowthLocalCommandTenantMiddleware</c> (primer uso
/// real, sin cambios de comportamiento) a BuildingBlocks.Web para reusar en los demás servicios.
///
/// <para>
/// <b>Bug real encontrado en producción/dev (2026-07-22), root-caused en dos rondas</b>: la primera
/// ronda de este fix asumió que estampar <c>IMessageBus.TenantId</c> en el scope de un handler haría
/// que Wolverine copiara ese valor al <see cref="Envelope.TenantId"/> de cualquier mensaje que ESE
/// handler publicara después (<c>bus.PublishAsync</c>) — el mismo mecanismo documentado para
/// <c>bus.InvokeAsync</c> desde un controller HTTP. Verificación end-to-end real (levantando los 14
/// servicios y triggereando el flujo real de Scribe) demostró que esa asunción es FALSA para
/// <c>PublishAsync</c> dentro de un handler: <c>IMessageBus.TenantId</c> es la propiedad que
/// Wolverine usa para su propio mecanismo nativo de multi-tenancy (enrutar a stores de mensajería
/// por tenant), no un valor que se copie automáticamente al envelope de un mensaje recién publicado.
/// <c>ScanFileCommand</c> seguía llegando a <c>ScanFileHandler</c> con <c>TenantContext</c> vacío
/// incluso con <c>bus.TenantId</c> ya estampado en el publicador — el fail-closed de EF seguía
/// escondiendo la fila real y el archivo quedaba colgado en <c>PendingScan</c> para siempre.
/// </para>
///
/// <para>
/// <b>Segunda ronda</b>: en vez de depender del envelope, este middleware pasó a leer el tenant
/// PRIMERO del propio mensaje — casi todo comando local en el monorepo ya trae su tenant como
/// primer parámetro posicional (<c>record ScanFileCommand(Guid TenantId, ...)</c>), exactamente el
/// mismo patrón que <see cref="IntegrationEventTenantMiddleware"/> ya usaba con éxito para los
/// integration events consumidos vía <see cref="Messaging.IIntegrationEvent"/>. Se lee por
/// reflexión (una propiedad pública <c>Guid TenantId</c>, cacheada por tipo). <see cref="Envelope.TenantId"/>
/// queda como fallback para el caso HTTP→<c>InvokeAsync</c> y para mensajes sin <c>TenantId</c> propio.
/// </para>
///
/// <para>
/// <b>Root cause real, confirmado con logging directo (no más teoría)</b>: este middleware SÍ corre y
/// SÍ llama <c>tenantContext.SetTenant(...)</c> con el valor correcto — confirmado instrumentando
/// tanto <c>Before</c> como el getter de <c>CloudStorageDbContext.EffectiveTenantId</c> con el hash de
/// instancia de <see cref="TenantContext"/> en cada punto. Para <c>ScanFileCommand</c> (publicado
/// desde dentro de <c>SaveFileFromSourceHandler</c> vía <c>bus.PublishAsync</c>, encolado localmente
/// con outbox durable), Wolverine ejecuta el middleware y el handler+sus dependencias en DOS SCOPES
/// DE DI DISTINTOS — el <see cref="TenantContext"/> que ve <c>ScanFileHandler</c>'s <c>DbContext</c>
/// NO es el mismo objeto que este middleware acaba de poblar, así que el filtro fail-closed de EF
/// (<c>EffectiveTenantId</c>) seguía viendo <c>HasTenant=false</c> pese a que el middleware había
/// corrido "correctamente" en su propio scope. Por eso el fix real no está en este archivo — está en
/// que <c>FileObjectRepository.GetAsync</c> (y análogos) usan <c>IgnoreQueryFilters()</c> explícito
/// cuando el caller YA trae un <c>tenantId</c> validado como parámetro (mismo patrón que
/// <c>ListExpiredUploadsAsync</c>/<c>GetByTokenHashAsync</c> en <c>CloudStorageDbContext</c>). Este
/// middleware se queda como mejora real (lee del mensaje en vez de asumir propagación de
/// <c>bus.TenantId</c>) pero NO se puede confiar en que el <see cref="TenantContext"/> que pobla
/// llegue siempre al mismo scope que ejecuta el handler para comandos locales encolados.
/// </para>
/// </summary>
public static class LocalCommandTenantMiddleware
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> TenantIdProperties = new();

    public static void Before(Envelope envelope, TenantContext tenantContext, IMessageBus bus)
    {
        var tenantId = ReadTenantIdFromMessage(envelope.Message);

        if (tenantId is null && Guid.TryParse(envelope.TenantId, out var fromEnvelope) && fromEnvelope != Guid.Empty)
            tenantId = fromEnvelope;

        if (tenantId is null || tenantId == Guid.Empty)
            return;

        tenantContext.SetTenant(tenantId.Value);
        bus.TenantId = tenantId.Value.ToString();
    }

    private static Guid? ReadTenantIdFromMessage(object? message)
    {
        if (message is null)
            return null;

        var property = TenantIdProperties.GetOrAdd(
            message.GetType(),
            static type => type.GetProperty("TenantId", typeof(Guid))
        );

        return property?.GetValue(message) as Guid?;
    }
}
