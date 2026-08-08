namespace TaxVision.Billing.Domain.ValueObjects;

/// <summary>Copia congelada de la identidad del cliente al emitir la factura. El CustomerId es
/// el GUID real del maestro Customer (a diferencia del CRM legado, que lo escondía en TaxId).</summary>
public sealed record CustomerSnapshot(
    Guid CustomerId,
    string Name,
    string? Email,
    string? Phone,
    string? TaxId,
    Address? Billing
);
