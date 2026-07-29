namespace TaxVision.Billing.Domain.ValueObjects;

/// <summary>Dirección postal usada en snapshots de cliente/emisor.</summary>
public sealed record Address(string Line1, string? Line2, string City, string State, string Zip, string Country);

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

/// <summary>Copia congelada de la identidad del emisor (el tenant). El logo se referencia por
/// FileId de CloudStorage, no en base64 inline como en el legado.</summary>
public sealed record IssuerSnapshot(
    string Name,
    Address Address,
    string? Phone,
    string? Email,
    string? Website,
    Guid? LogoFileId,
    string? TaxId = null
);

/// <summary>Descuento a nivel de factura. Value = basis points (Percentage) o cents (Fixed).
/// Amount = monto aplicado congelado.</summary>
public sealed record Discount(DiscountType Type, int Value, Money Amount);
