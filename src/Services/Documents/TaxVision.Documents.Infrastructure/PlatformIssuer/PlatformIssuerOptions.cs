namespace TaxVision.Documents.Infrastructure.PlatformIssuer;

/// <summary>Datos de la entidad legal que emite lo que factura la plataforma (recibo de onboarding), no un
/// tenant. Nombre, correo y web NO van acá: salen de PlatformSettings (ver PlatformIssuerProvider).</summary>
public sealed class PlatformIssuerOptions
{
    public const string SectionName = "Documents:PlatformIssuer";

    public string TaxId { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "US";
    public string Phone { get; set; } = string.Empty;
    public string? LogoDataUri { get; set; }
}
