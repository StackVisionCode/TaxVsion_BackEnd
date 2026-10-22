using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Domain.Providers;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

// <summary>Options for configuring Google Drive OAuth.</summary>
public sealed class GoogleDriveOptions
{
    public const string SectionName = "CloudStorage:GoogleDrive";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string FrontendReturnUri { get; set; } = string.Empty;
}

// <summary>Service for connecting a tenant to Google Drive via OAuth.</summary>
public interface IGoogleDriveConnectionService
{
    Task<string> StartAsync(Guid tenantId, Guid userId, string? returnOrigin, CancellationToken ct);
    Task CompleteAsync(Guid stateId, string code, CancellationToken ct);
    Task<(bool Connected, string? Email)> GetStatusAsync(Guid tenantId, CancellationToken ct);
    Task DisconnectAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>OAuth Google Drive, con consentimiento drive.file y credenciales cifradas por tenant.</summary>
public sealed class GoogleDriveConnectionService(
    CloudStorageDbContext db,
    IHttpClientFactory clients,
    IServiceProvider services,
    CloudProviderOAuthStateStore states,
    IOptions<GoogleDriveOptions> options
) : IGoogleDriveConnectionService
{
    private const string Scope = "openid email https://www.googleapis.com/auth/drive.file";

    // <summary>Starts the OAuth flow by creating a nonce and returning the Google authorization URL.</summary>
    public async Task<string> StartAsync(Guid tenantId, Guid userId, string? returnOrigin, CancellationToken ct)
    {
        var config = options.Value;
        EnsureConfigured(config);
        // Sin Encryption:MasterKey el token no se puede guardar: fallar aquí, antes del consentimiento.
        _ = services.GetRequiredService<ISecretProtector>();
        var state = await states.IssueAsync(tenantId, userId, "google-drive", returnOrigin, ct);
        var values = new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["redirect_uri"] = config.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["state"] = state.Id.ToString("N"),
            ["access_type"] = "offline",
            ["prompt"] = "consent select_account",
        };
        return "https://accounts.google.com/o/oauth2/v2/auth?"
            + string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    public async Task CompleteAsync(Guid stateId, string code, CancellationToken ct)
    {
        var state =
            await states.ConsumeAsync(stateId, "google-drive", ct)
            ?? throw new InvalidOperationException("The Google authorization request expired. Please reconnect.");

        var config = options.Value;
        EnsureConfigured(config);
        using var tokenResponse = await clients
            .CreateClient("google-drive-oauth")
            .PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["client_id"] = config.ClientId,
                        ["client_secret"] = config.ClientSecret,
                        ["code"] = code,
                        ["redirect_uri"] = config.RedirectUri,
                        ["grant_type"] = "authorization_code",
                    }
                ),
                ct
            );
        tokenResponse.EnsureSuccessStatusCode();
        var token =
            await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Google did not return an OAuth token.");
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException(
                "Google did not return offline access. Revoke the existing grant and reconnect."
            );

        using var profileRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://openidconnect.googleapis.com/v1/userinfo"
        );
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var profileResponse = await clients.CreateClient("google-drive-oauth").SendAsync(profileRequest, ct);
        profileResponse.EnsureSuccessStatusCode();
        var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileResponse>(cancellationToken: ct);
        if (string.IsNullOrWhiteSpace(profile?.Email) || profile.EmailVerified != true)
            throw new InvalidOperationException("Google did not return a verified email address.");

        var old = await db
            .CloudConnections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == state.TenantId && x.Provider == "google-drive", ct);
        if (old is not null)
            db.CloudConnections.Remove(old);
        var protector = services.GetRequiredService<ISecretProtector>();
        db.CloudConnections.Add(
            TenantCloudConnection.Connect(state.TenantId, profile.Email, protector.Protect(token.RefreshToken))
        );
        await db.SaveChangesAsync(ct);
    }

    public async Task<(bool Connected, string? Email)> GetStatusAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await db
            .CloudConnections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Provider == "google-drive", ct);
        return (connection is not null, connection?.AccountEmail);
    }

    // <summary>Revokes the refresh token and removes the connection record.</summary>
    public async Task DisconnectAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await db.CloudConnections.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Provider == "google-drive",
            ct
        );
        if (connection is null)
            return;
        var protector = services.GetRequiredService<ISecretProtector>();
        if (protector.TryUnprotect(connection.ProtectedRefreshToken, out var token, out _))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/revoke")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }),
            };
            try
            {
                using var response = await clients.CreateClient("google-drive-oauth").SendAsync(request, ct);
            }
            catch (HttpRequestException) { }
        }
        db.CloudConnections.Remove(connection);
        await db.SaveChangesAsync(ct);
    }

    // <summary>Throws if the Google Drive OAuth configuration is missing or incomplete.</summary>
    private static void EnsureConfigured(GoogleDriveOptions config)
    {
        if (
            string.IsNullOrWhiteSpace(config.ClientId)
            || string.IsNullOrWhiteSpace(config.ClientSecret)
            || string.IsNullOrWhiteSpace(config.RedirectUri)
            || string.IsNullOrWhiteSpace(config.FrontendReturnUri)
        )
            throw new InvalidOperationException("Google Drive OAuth is not configured for this environment.");
    }

    // <summary>Represents the response from Google's OAuth token endpoint.</summary>
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken
    );

    private sealed record ProfileResponse(
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("email_verified")] bool? EmailVerified
    );
}
