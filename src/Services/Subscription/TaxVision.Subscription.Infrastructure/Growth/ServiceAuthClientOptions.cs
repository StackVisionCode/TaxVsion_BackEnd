namespace TaxVision.Subscription.Infrastructure.Growth;

/// <summary>Credenciales client-credentials para obtener tokens M2M desde Auth.</summary>
public sealed class ServiceAuthClientOptions
{
    public const string SectionName = "Subscription:ServiceAuth";

    public string AuthBaseUrl { get; set; } = "http://localhost:5124";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>Base URL del microservicio Growth.</summary>
public sealed class GrowthClientOptions
{
    public const string SectionName = "Subscription:Growth";

    public string BaseUrl { get; set; } = "http://localhost:5187";
}
