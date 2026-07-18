using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.Providers.OAuth;

/// <summary>Refresh_token grant y authorization_code grant (flujo de conectar cuenta) contra el token endpoint estándar de Google OAuth2 — mismo endpoint, distinto grant_type.</summary>
public sealed class GoogleOAuthClient(
    HttpClient httpClient,
    IOptions<GoogleOAuthOptions> options,
    ILogger<GoogleOAuthClient> logger
) : IOAuthProviderClient
{
    public ProviderCode ProviderCode => ProviderCode.Gmail;
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
            throw new OAuthProviderException("Google userinfo request failed (network error).", ex);
        }

        if (!response.IsSuccessStatusCode)
            throw new OAuthProviderException($"Google userinfo request returned HTTP {(int)response.StatusCode}.");

        GoogleUserInfoResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<GoogleUserInfoResponse>(ct);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new OAuthProviderException("Google userinfo response was unparseable.", ex);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Email))
            throw new OAuthProviderException("Google userinfo response was missing email.");

        return payload.Email;
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
            $"state={Uri.EscapeDataString(state)}",
            // access_type=offline+prompt=consent: única forma de garantizar refresh_token incluso si
            // el usuario ya autorizó la app antes (sin esto Google no lo re-emite).
            "access_type=offline",
            "prompt=consent"
        );
        return $"{config.AuthorizationEndpoint}?{query}";
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://oauth2.googleapis.com/revoke?token={Uri.EscapeDataString(refreshToken)}"
            );
            await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "Google token revocation failed (best-effort, account is still disconnected locally)."
            );
        }
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
            throw new OAuthProviderException("Google OAuth token request failed (network error).", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Google OAuth token request returned HTTP {Status}.", (int)response.StatusCode);
            throw new OAuthProviderException($"Google OAuth token request returned HTTP {(int)response.StatusCode}.");
        }

        GoogleTokenResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(ct);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new OAuthProviderException("Google OAuth token request returned an unparseable response.", ex);
        }

        if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
            throw new OAuthProviderException("Google OAuth token response was missing access_token.");

        return new OAuthTokenGrant(payload.AccessToken, payload.RefreshToken, payload.ExpiresInSeconds);
    }

    private sealed record GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresInSeconds { get; init; }
    }

    private sealed record GoogleUserInfoResponse
    {
        [JsonPropertyName("email")]
        public string? Email { get; init; }
    }
}
