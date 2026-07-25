namespace BuildingBlocks.Domain;

/// <summary>
/// Variante de <see cref="ITenantOwned"/> para entidades System-or-Tenant scoped, donde
/// <c>TenantId</c> es <see cref="Guid"/>? — null identifica una fila System-scope (visible para
/// cualquier tenant), no-null una fila Tenant-scope (visible solo para ese tenant). El
/// <c>HasQueryFilter</c> genérico para estas entidades filtra por
/// <c>TenantId == null || TenantId == EffectiveTenantId</c> en vez de la igualdad estricta de
/// <see cref="ITenantOwned"/> (RBAC Fase 5, primer caso real: Scribe EmailTemplate/EmailLayout/
/// EventTemplateMapping).
/// </summary>
public interface INullableTenantOwned
{
    Guid? TenantId { get; }
}
