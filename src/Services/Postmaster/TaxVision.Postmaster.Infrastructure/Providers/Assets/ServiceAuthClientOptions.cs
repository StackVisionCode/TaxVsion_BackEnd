namespace TaxVision.Postmaster.Infrastructure.Providers.Assets;

/// <summary>Credenciales client-credentials para obtener tokens M2M desde Auth.</summary>
public sealed class ServiceAuthClientOptions
{
    public const string SectionName = "Postmaster:ServiceAuth";

    public string AuthBaseUrl { get; set; } = "http://localhost:5124";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>Base URL del microservicio CloudStorage.</summary>
public sealed class CloudStorageClientOptions
{
    public const string SectionName = "Postmaster:CloudStorage";

    public string BaseUrl { get; set; } = "http://localhost:5210";
}
