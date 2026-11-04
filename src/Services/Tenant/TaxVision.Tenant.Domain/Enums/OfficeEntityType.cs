namespace TaxVision.Tenant.Domain.Enums;

/// <summary>Forma jurídica de la oficina. Se guarda como texto: agregar un valor no requiere migración.</summary>
public enum OfficeEntityType
{
    SoleProprietor,
    Llc,
    Partnership,
    SCorp,
    CCorp,
    Nonprofit,
    Other,
}
