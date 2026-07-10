namespace TaxVision.Signature.Infrastructure.Sealing.HttpClients;

/// <summary>Credenciales client-credentials para obtener tokens M2M desde Auth (workers background).</summary>
public sealed class ServiceAuthClientOptions
{
    public const string SectionName = "Signature:ServiceAuth";

    public string AuthBaseUrl { get; set; } = "http://localhost:5124";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>Base URL del microservicio CloudStorage.</summary>
public sealed class CloudStorageClientOptions
{
    public const string SectionName = "Signature:CloudStorage";

    public string BaseUrl { get; set; } = "http://localhost:5210";
}
