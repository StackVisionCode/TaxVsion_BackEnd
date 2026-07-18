using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.OAuth;

/// <summary>Resultado de un refresh_token grant OAuth2. RefreshToken es null si el proveedor no rotó uno nuevo.</summary>
public sealed record OAuthTokenGrant(string AccessToken, string? RefreshToken, int ExpiresInSeconds);

/// <summary>
/// Lanzada por <see cref="IOAuthProviderClient"/> ante cualquier fallo del refresh (HTTP, red,
/// respuesta inesperada). Deliberado: nunca un Result.Failure — así el caller puede envolver la
/// llamada en un circuit breaker Polly, que solo cuenta fallos vía excepción (mismo patrón que
/// SmtpEmailSender en Postmaster).
/// </summary>
public sealed class OAuthProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Un client por proveedor OAuth2 (Google, Microsoft) que sabe hacer el refresh_token grant.
/// ClientId/ClientSecret son del app registration compartido de TaxVision (un registro por
/// proveedor, no por tenant) — el client los toma de su propia config, no los recibe acá.
/// </summary>
public interface IOAuthProviderClient
{
    ProviderCode ProviderCode { get; }

    /// <summary>Client id del app registration compartido (nunca el secret) — flujo de conectar cuenta (D3 §12.5), se persiste en <c>OAuthConnection.ClientId</c>.</summary>
    string ClientId { get; }

    /// <summary>El scope que este servicio siempre pide (§5.4 del design doc de D3 — todo junto desde el connect inicial, nunca incremental) — se persiste en <c>OAuthConnection.Scope</c>.</summary>
    string ConfiguredScope { get; }

    /// <summary>
    /// El mismo redirect_uri configurado en el app registration del proveedor — nunca se acepta uno
    /// distinto desde el request (D3 §12.4/12.5), tanto para construir la authorization URL como para
    /// el token exchange, evitando una clase entera de bugs de redirect_uri manipulado por el caller.
    /// </summary>
    string RedirectUri { get; }

    Task<OAuthTokenGrant> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// Intercambia el <c>code</c> del callback de autorización (flujo de conectar cuenta) por el
    /// primer access+refresh token — mismo token endpoint que <see cref="RefreshAccessTokenAsync"/>,
    /// grant_type <c>authorization_code</c> en vez de <c>refresh_token</c>. <paramref name="redirectUri"/>
    /// debe ser exactamente el mismo que se usó para construir la authorization URL (exigencia del
    /// estándar OAuth2, no específico de ningún proveedor).
    /// </summary>
    Task<OAuthTokenGrant> ExchangeAuthorizationCodeAsync(
        string code,
        string redirectUri,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resuelve la dirección de correo real dueña de <paramref name="accessToken"/> (Google userinfo
    /// endpoint / Graph <c>/me</c>) — flujo de conectar cuenta (D3 §12.5). Nunca se confía en un email
    /// que mande el frontend; debe ser el que el proveedor mismo confirma.
    /// </summary>
    Task<string> GetAuthorizedEmailAddressAsync(string accessToken, CancellationToken ct = default);

    /// <summary>
    /// La URL a la que el frontend redirige el navegador del usuario para iniciar el consentimiento
    /// (D3 §12.4, <c>POST /connectors/accounts</c>) — incluye <paramref name="state"/> (CSRF, un solo
    /// uso, ver <see cref="IOAuthConnectStateStore"/>). Google fuerza <c>access_type=offline&amp;prompt=consent</c>
    /// (única forma de garantizar un refresh_token incluso si el usuario ya autorizó la app antes);
    /// Microsoft no lo necesita porque <c>offline_access</c> ya va en el scope configurado.
    /// </summary>
    string BuildAuthorizationUrl(string state);

    /// <summary>
    /// Revoca el grant del lado del proveedor, best-effort (D3 §12.4, <c>DELETE /connectors/accounts/{id}</c>)
    /// — nunca lanza ni bloquea la desconexión local si falla. Default no-op: Graph no expone una API
    /// de revocación equivalente vía Microsoft Graph (la única forma real es que el usuario la revoque
    /// él mismo desde myaccount.microsoft.com/consents) — solo Google la sobreescribe.
    /// </summary>
    Task RevokeAsync(string refreshToken, CancellationToken ct = default) => Task.CompletedTask;
}
