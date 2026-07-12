namespace TaxVision.Notification.Infrastructure.Providers;

/// <summary>
/// Credenciales de las apps OAuth para refrescar el access token de las cuentas Gmail/Graph. El token
/// inicial se obtiene fuera (flujo OAuth del frontend) y se pasa en el connect; aquí solo se refresca.
/// Se configuran en secret store / .env; nunca con valores reales en el repo.
/// </summary>
public sealed class EmailOAuthOptions
{
    public const string SectionName = "EmailOAuth";

    public OAuthAppConfig Gmail { get; set; } = new();
    public MicrosoftOAuthConfig Microsoft { get; set; } = new();
}

public class OAuthAppConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class MicrosoftOAuthConfig : OAuthAppConfig
{
    /// <summary>Tenant de Azure AD (o "common"/"organizations").</summary>
    public string TenantId { get; set; } = "common";

    public string Scope { get; set; } = "https://graph.microsoft.com/.default offline_access";
}
