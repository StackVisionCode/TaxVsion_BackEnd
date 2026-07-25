using BuildingBlocks.Domain;

namespace TaxVision.Correspondence.Domain.Backfill;

/// <summary>
/// Marca de "ya corrí el backfill de CustomerEmailAddresses para este tenant" (Fase 2). Un tenant
/// nunca es tenant-scoped al arrancar Correspondence — la única forma de descubrir un tenant es
/// verlo llegar en un evento de Customer (ver los 6 consumers en Application/Projections/
/// CustomerEvents/). La presencia de esta fila para un TenantId es la señal de "no volver a
/// pedirle a Customer.Api la lista completa de clientes de este tenant".
/// </summary>
public sealed class TenantBackfillState : ITenantOwned
{
    private TenantBackfillState() { }

    public Guid TenantId { get; private set; }
    public DateTime CompletedAtUtc { get; private set; }

    /// <summary>RBAC Fase 5 (RBAC_Hardening_Plan.md) — ver <see cref="Compose.Draft.SetTenant"/>.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static TenantBackfillState Create(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        return new TenantBackfillState { TenantId = tenantId, CompletedAtUtc = DateTime.UtcNow };
    }
}
