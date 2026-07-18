namespace TaxVision.Connectors.Infrastructure.Providers.OAuth;

/// <summary>
/// Credenciales del app registration de Microsoft Graph compartido por TaxVision (uno para todos
/// los tenants) — nunca por-tenant. TenantId "common" acepta cuentas de cualquier organización.
/// Mail.Send es DELEGADO (D3 §5.2 del design doc de OAuth send) — no requiere admin-consent por
/// default; AADSTS90094 solo aparece como fallback si el tenant tiene conditional access
/// restrictivo (ver AdminConsentEndpointTemplate).
/// </summary>
public sealed class MicrosoftOAuthOptions
{
    public const string SectionName = "Connectors:OAuth:Microsoft";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string TenantId { get; set; } = "common";
    public string TokenEndpointTemplate { get; set; } = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";
    public string Scope { get; set; } =
        "https://graph.microsoft.com/Mail.Read https://graph.microsoft.com/Mail.Send offline_access";

    public string AuthorizationEndpointTemplate { get; set; } =
        "https://login.microsoftonline.com/{0}/oauth2/v2.0/authorize";
    public string AdminConsentEndpointTemplate { get; set; } = "https://login.microsoftonline.com/{0}/adminconsent";

    /// <summary>Debe matchear byte a byte el redirect_uri configurado en el app registration de Microsoft y el usado en ExchangeAuthorizationCodeAsync.</summary>
    public string RedirectUri { get; set; } = string.Empty;
    public string AdminConsentRedirectUri { get; set; } = string.Empty;

    public string UserInfoEndpoint { get; set; } = "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName";

    public string TokenEndpoint => string.Format(TokenEndpointTemplate, TenantId);
    public string AuthorizationEndpoint => string.Format(AuthorizationEndpointTemplate, TenantId);
    public string AdminConsentEndpoint => string.Format(AdminConsentEndpointTemplate, TenantId);
}
