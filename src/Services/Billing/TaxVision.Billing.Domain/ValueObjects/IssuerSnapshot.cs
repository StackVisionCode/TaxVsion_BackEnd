namespace TaxVision.Billing.Domain.ValueObjects;

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
