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

    /// <summary>Inicia la secuencia del tenant/período en 1.</summary>
    public static InvoiceNumberSequence Start(Guid tenantId, string periodKey)
    {
        var sequence = new InvoiceNumberSequence { PeriodKey = periodKey, Next = 1 };
        sequence.SetTenant(tenantId);
        return sequence;
    }

    /// <summary>Reserva el próximo número y avanza el contador. La atomicidad bajo concurrencia la
    /// garantiza el RowVersion (optimistic concurrency) al persistir: un conflicto reintenta.</summary>
    public long Allocate()
    {
        var allocated = Next;
        Next += 1;
        return allocated;
    }
}
