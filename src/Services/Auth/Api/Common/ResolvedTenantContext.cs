namespace TaxVision.Auth.Api.Common;

/// <summary>
/// Tenant candidato resuelto desde el Host de la request (Fase A3), poblado por
/// TenantHostResolutionMiddleware. Es solo informativo: el TenantId autoritativo para
/// requests autenticadas sigue saliendo del claim "tenant_id" del JWT, nunca de este
/// candidato. No confundir con BuildingBlocks.Web.Tenancy.ITenantContext — ese es el
/// tenant autoritativo propagado vía X-Tenant-Id entre servicios ya autenticados; este
/// es solo la señal previa a autenticación que aporta el Host.
/// </summary>
public interface IResolvedTenantContext
{
    Guid? ResolvedTenantId { get; }

    void SetResolvedTenant(Guid tenantId);
}

public sealed class ResolvedTenantContext : IResolvedTenantContext
{
    public Guid? ResolvedTenantId { get; private set; }

    public void SetResolvedTenant(Guid tenantId) => ResolvedTenantId = tenantId;
}
