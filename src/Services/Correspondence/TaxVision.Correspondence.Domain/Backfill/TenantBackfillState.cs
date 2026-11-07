using BuildingBlocks.Domain;

namespace TaxVision.Correspondence.Domain.Backfill;

/// <summary>
/// Avance del backfill de CustomerEmailAddresses de un tenant (Fase 2, COR-08). Un tenant
/// nunca es tenant-scoped al arrancar Correspondence — la única forma de descubrir un tenant es
/// verlo llegar en un evento de Customer (ver los 6 consumers en Application/Projections/
/// CustomerEvents/). Con <see cref="CompletedAtUtc"/> puesto no se vuelve a pedirle a Customer.Api la
/// lista completa de clientes de este tenant.
/// </summary>
public sealed class TenantBackfillState : ITenantOwned
{
    private TenantBackfillState() { }

    public Guid TenantId { get; private set; }

    /// <summary>Null mientras el backfill sigue en curso (COR-08).</summary>
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>Última página de Customer guardada: un reintento sigue desde la próxima.</summary>
    public int LastConfirmedPage { get; private set; }

    public bool IsCompleted => CompletedAtUtc is not null;

    /// <summary>RBAC Fase 5 (RBAC_Hardening_Plan.md) — ver <see cref="Compose.Draft.SetTenant"/>.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static TenantBackfillState Start(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        return new TenantBackfillState { TenantId = tenantId };
    }

    public void ConfirmPage(int page, bool isLastPage)
    {
        LastConfirmedPage = page;
        if (isLastPage)
            CompletedAtUtc = DateTime.UtcNow;
    }
}
