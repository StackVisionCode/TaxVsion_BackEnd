namespace TaxVision.Correspondence.Infrastructure.Customers;

/// <summary>Credenciales client-credentials para obtener tokens M2M desde Auth (worker background).</summary>
public sealed class ServiceAuthClientOptions
{
    public const string SectionName = "Correspondence:ServiceAuth";

    public string AuthBaseUrl { get; set; } = "http://localhost:5124";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>Base URL del microservicio Customer.</summary>
public sealed class CustomerClientOptions
{
    public const string SectionName = "Correspondence:Customer";

    public string BaseUrl { get; set; } = "http://localhost:5263";
}
