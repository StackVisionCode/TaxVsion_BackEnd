using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Backfill;

/// <summary>
/// Marca de «ya corrí el backfill del directorio de customers para este tenant». Task no conoce
/// ningún tenant al arrancar: la única forma de descubrir uno es verlo llegar en un evento de
/// Customer, porque no hay endpoint M2M que enumere tenants. La existencia de esta fila es la señal
/// de no volver a pedirle a Customer la lista completa.
/// </summary>
public sealed class TenantBackfillState : ITenantOwned
{
    private TenantBackfillState() { }

    public Guid TenantId { get; private set; }
    public DateTime CompletedAtUtc { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static TenantBackfillState Create(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        return new TenantBackfillState { TenantId = tenantId, CompletedAtUtc = DateTime.UtcNow };
    }
}
