namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>CAT-F5.2 — Publicado por Subscription cuando el admin edita el catálogo (planes o
/// add-ons). A diferencia de <see cref="TenantEntitlementsChangedIntegrationEvent"/>, que se emite
/// por tenant tras recalcular sus entitlements, este evento es **tenant-agnóstico**: anuncia
/// cambios en el catálogo público (crear plan, publicar versión, cambiar precio, archivar,
/// crear add-on, revisar módulos) que afectan al <b>listado</b> que todos los frontends muestran.
///
/// <para>Un consumer en Communication empuja un evento socket <c>catalog:changed</c> a los rooms
/// de tenants + landing anónimo, para que los frontends refetcheen <c>GET /subscription/plans</c> y
/// <c>GET /subscription/addons</c> sin esperar a que el usuario recargue manual.</para>
///
/// <para><see cref="ChangeKind"/> es informativo (lo que cambió). Los consumers no deberían
/// reaccionar distinto según el kind — todos refetchean el listado completo. Si en el futuro hace
/// falta reaccionar diferente, se agrega sin romper (el consumer puede ignorar valores desconocidos).</para>
/// </summary>
public sealed record CatalogChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>Qué parte del catálogo cambió: <c>plan.created</c>, <c>plan.metadata</c>,
    /// <c>plan.version_published</c>, <c>plan.deprecated</c>, <c>plan.archived</c>, <c>addon.created</c>,
    /// <c>addon.prices</c>. Informativo — los consumers refetchean todo el catálogo igual.</summary>
    public required string ChangeKind { get; init; }

    /// <summary>Id del plan o add-on que cambió. Null si el evento es "catalog bulk reconcile".</summary>
    public Guid? EntityId { get; init; }

    /// <summary>Code del plan o add-on. Útil para logs.</summary>
    public string? EntityCode { get; init; }
}
