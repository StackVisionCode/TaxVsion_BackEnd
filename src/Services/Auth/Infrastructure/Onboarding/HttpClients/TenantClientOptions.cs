namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>PayFlow (Fase 14) — base URL del servicio Tenant, usado por
/// <see cref="TenantSubdomainAvailabilityClient"/>.</summary>
public sealed class TenantClientOptions
{
    public const string SectionName = "Auth:Tenant";

    public string BaseUrl { get; set; } = "http://localhost:5217";
}
