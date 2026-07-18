namespace TaxVision.Connectors.Infrastructure.Providers.OAuth;

/// <summary>
/// Credenciales del app registration de Google compartido por TaxVision (uno para todos los
/// tenants) — nunca por-tenant. Ver Connectors_Service_Design_And_Implementation_Plan.md §27.
/// </summary>
public sealed class GoogleOAuthOptions
{
    public const string SectionName = "Connectors:OAuth:Google";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";

    /// <summary>Flujo de conectar cuenta (D3 §12.2) — gmail.readonly (leer, Fase 5) + gmail.send (D3, least-privilege confirmado por Google para "solo enviar").</summary>
    public string AuthorizationEndpoint { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";
    public string Scope { get; set; } =
        "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.send";

    /// <summary>Debe matchear byte a byte el redirect_uri configurado en el app registration de Google y el usado en ExchangeAuthorizationCodeAsync.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    public string UserInfoEndpoint { get; set; } = "https://www.googleapis.com/oauth2/v2/userinfo";
}
