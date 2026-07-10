using System.Text.Json;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Infrastructure.Providers;

/// <summary>
/// Entrega un access token válido para una cuenta OAuth (Gmail/Graph), refrescándolo con el refresh token
/// si está por expirar. El nuevo token se cifra y se guarda en la cuenta (la persiste el servicio de sync).
/// </summary>
public sealed class OAuthTokenService(
    ISecretProtector protector,
    IHttpClientFactory httpClientFactory,
    IOptions<EmailOAuthOptions> options,
    ILogger<OAuthTokenService> logger
)
{
    public async Task<string?> GetValidAccessTokenAsync(EmailAccountConnection account, CancellationToken ct)
    {
        var access = string.IsNullOrEmpty(account.AccessTokenCipher)
            ? null
            : protector.Unprotect(account.AccessTokenCipher);

        var nearExpiry = account.TokenExpiresAtUtc is { } exp && exp <= DateTime.UtcNow.AddSeconds(60);
        if ((access is null || nearExpiry) && !string.IsNullOrEmpty(account.RefreshTokenCipher))
        {
            var refresh = protector.Unprotect(account.RefreshTokenCipher);
            if (!string.IsNullOrEmpty(refresh))
            {
                var refreshed = await RefreshAsync(account.Provider, refresh, ct);
                if (refreshed is not null)
                {
                    account.UpdateTokens(
                        protector.Protect(refreshed.AccessToken),
                        string.IsNullOrEmpty(refreshed.RefreshToken) ? null : protector.Protect(refreshed.RefreshToken),
                        DateTime.UtcNow.AddSeconds(refreshed.ExpiresIn)
                    );
                    return refreshed.AccessToken;
                }
            }
        }

        return access;
    }

    private async Task<RefreshedToken?> RefreshAsync(
        EmailExternalProvider provider,
        string refreshToken,
        CancellationToken ct
    )
    {
        var opt = options.Value;
        var (endpoint, app, extra) = provider switch
        {
            EmailExternalProvider.GmailApi => (
                "https://oauth2.googleapis.com/token",
                (OAuthAppConfig)opt.Gmail,
                (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()
            ),
            EmailExternalProvider.MicrosoftGraph => (
                $"https://login.microsoftonline.com/{opt.Microsoft.TenantId}/oauth2/v2.0/token",
                opt.Microsoft,
                new Dictionary<string, string> { ["scope"] = opt.Microsoft.Scope }
            ),
            _ => (string.Empty, new OAuthAppConfig(), new Dictionary<string, string>()),
        };

        if (
            string.IsNullOrEmpty(endpoint)
            || string.IsNullOrWhiteSpace(app.ClientId)
            || string.IsNullOrWhiteSpace(app.ClientSecret)
        )
        {
            logger.LogWarning("OAuth app for {Provider} is not configured; cannot refresh the token.", provider);
            return null;
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = app.ClientId,
            ["client_secret"] = app.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        };
        foreach (var (k, v) in extra)
            form[k] = v;

        try
        {
            using var client = httpClientFactory.CreateClient("email-oauth");
            using var response = await client.PostAsync(endpoint, new FormUrlEncodedContent(form), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Token refresh for {Provider} failed ({Status}).",
                    provider,
                    (int)response.StatusCode
                );
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var accessEl))
                return null;

            var expires = root.TryGetProperty("expires_in", out var expEl) ? expEl.GetInt32() : 3600;
            var newRefresh = root.TryGetProperty("refresh_token", out var rtEl) ? rtEl.GetString() : null;
            return new RefreshedToken(accessEl.GetString()!, newRefresh, expires);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token refresh for {Provider} threw.", provider);
            return null;
        }
    }

    private sealed record RefreshedToken(string AccessToken, string? RefreshToken, int ExpiresIn);
}
