namespace TaxVision.Documents.Infrastructure.PlatformIssuer;

/// <summary>Datos fijos del emisor plataforma (TaxVision Inc.) para documentos que la plataforma
/// misma emite (recibo de onboarding), no un tenant. Config real por ambiente.</summary>
public sealed class PlatformIssuerOptions
{
    public const string SectionName = "Documents:PlatformIssuer";

    public string Name { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "US";
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string? LogoDataUri { get; set; }
}
