namespace TaxVision.Auth.Application.ServiceTokens;

/// <summary>
/// Clientes de servicio (M2M) autorizados a solicitar tokens client-credentials. Se configuran en
/// <c>ServiceAuth:Clients</c> (secret store / .env), nunca con secretos reales en el repositorio.
/// </summary>
public sealed class ServiceAuthOptions
{
    public const string SectionName = "ServiceAuth";

    public List<ServiceClientConfig> Clients { get; set; } = [];

    /// <summary>Minutos de vigencia del token de servicio emitido.</summary>
    public int TokenLifetimeMinutes { get; set; } = 10;
}

public sealed class ServiceClientConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = [];
}
