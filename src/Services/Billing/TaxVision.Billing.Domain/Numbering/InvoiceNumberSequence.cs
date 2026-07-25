using BuildingBlocks.Domain;

namespace TaxVision.Billing.Domain.Numbering;

/// <summary>Contador monótono server-side de numeración de facturas por tenant (y período). Clave
/// (TenantId, PeriodKey). Corrige el número client-supplied del CRM legado. SCAFFOLD B1:
/// la asignación concurrente (Allocate con RowVersion) se implementa en B2.</summary>
public sealed class InvoiceNumberSequence : TenantEntity
{
    public string PeriodKey { get; private set; } = "ALL";
    public long Next { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private InvoiceNumberSequence() { }
}
