using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.Providers.OAuth;

/// <summary>Refresh_token grant y authorization_code grant (flujo de conectar cuenta) contra el token endpoint v2 de Microsoft identity platform (Graph) — mismo endpoint, distinto grant_type.</summary>
public sealed class MicrosoftOAuthClient(
    HttpClient httpClient,
    IOptions<MicrosoftOAuthOptions> options,
    ILogger<MicrosoftOAuthClient> logger
) : IOAuthProviderClient, IMicrosoftAdminConsentClient
{
    public ProviderCode ProviderCode => ProviderCode.Graph;
    public string ClientId => options.Value.ClientId;
    public string ConfiguredScope => options.Value.Scope;
    public string RedirectUri => options.Value.RedirectUri;

    public Task<OAuthTokenGrant> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var config = options.Value;
        return SendTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
                ["scope"] = config.Scope,
            },
            ct
        );
    }

    public Task<OAuthTokenGrant> ExchangeAuthorizationCodeAsync(
        string code,
        string redirectUri,
        CancellationToken ct = default
    )
    {
        var config = options.Value;
        return SendTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
                ["scope"] = config.Scope,
            },
            ct
        );
    }

    public async Task<string> GetAuthorizedEmailAddressAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.Value.UserInfoEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new OAuthProviderException("Microsoft /me request failed (network error).", ex);
        }

        if (!response.IsSuccessStatusCode)
            throw new OAuthProviderException($"Microsoft /me request returned HTTP {(int)response.StatusCode}.");

        MicrosoftUserInfoResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<MicrosoftUserInfoResponse>(ct);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new OAuthProviderException("Microsoft /me response was unparseable.", ex);
        }

        // 'mail' es null para algunas cuentas (p. ej. cuentas personales sin buzón Exchange
        // configurado con esa propiedad) — userPrincipalName es el fallback documentado por Microsoft.
        var email = payload?.Mail ?? payload?.UserPrincipalName;
        if (string.IsNullOrWhiteSpace(email))
            throw new OAuthProviderException("Microsoft /me response was missing mail and userPrincipalName.");

        return email;
    }

    public string BuildAuthorizationUrl(string state)
    {
        var config = options.Value;
        var query = string.Join(
            '&',
            $"client_id={Uri.EscapeDataString(config.ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(config.RedirectUri)}",
            "response_type=code",
            $"scope={Uri.EscapeDataString(config.Scope)}",
            $"state={Uri.EscapeDataString(state)}"
        );
        return $"{config.AuthorizationEndpoint}?{query}";
    }

    public string BuildAdminConsentUrl(string state)
    {
        var config = options.Value;
        var query = string.Join(
            '&',
            $"client_id={Uri.EscapeDataString(config.ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(config.AdminConsentRedirectUri)}",
            $"state={Uri.EscapeDataString(state)}"
        );
        return $"{config.AdminConsentEndpoint}?{query}";
    }

    private async Task<OAuthTokenGrant> SendTokenRequestAsync(
        Dictionary<string, string> parameters,
        CancellationToken ct
    )
    {
        var config = options.Value;
        var body = new FormUrlEncodedContent(parameters);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(config.TokenEndpoint, body, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new OAuthProviderException("Microsoft OAuth token request failed (network error).", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Microsoft OAuth token request returned HTTP {Status}.", (int)response.StatusCode);
            throw new OAuthProviderException(
                $"Microsoft OAuth token request returned HTTP {(int)response.StatusCode}."
            );
        }

        MicrosoftTokenResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<MicrosoftTokenResponse>(ct);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new OAuthProviderException("Microsoft OAuth token request returned an unparseable response.", ex);
        }

        if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
            throw new OAuthProviderException("Microsoft OAuth token response was missing access_token.");

        return new OAuthTokenGrant(payload.AccessToken, payload.RefreshToken, payload.ExpiresInSeconds);
    }

    private sealed record MicrosoftTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        // Microsoft rota el refresh_token en casi todas las respuestas (a diferencia de Google).
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresInSeconds { get; init; }
    }

    private sealed record MicrosoftUserInfoResponse
    {
        [JsonPropertyName("mail")]
        public string? Mail { get; init; }

        [JsonPropertyName("userPrincipalName")]
        public string? UserPrincipalName { get; init; }
    }
}
