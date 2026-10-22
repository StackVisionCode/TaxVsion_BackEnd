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

public sealed class DropboxOptions
{
    public const string SectionName = "CloudStorage:Dropbox";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string FrontendReturnUri { get; set; } = string.Empty;
}

public interface IDropboxConnectionService
{
    Task<string> StartAsync(Guid tenantId, Guid userId, string? returnOrigin, CancellationToken ct);
    Task CompleteAsync(Guid stateId, string code, CancellationToken ct);
    Task<(bool Connected, string? Email)> GetStatusAsync(Guid tenantId, CancellationToken ct);
    Task DisconnectAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>Dropbox OAuth authorization-code flow with tenant-scoped encrypted refresh tokens.</summary>
public sealed class DropboxConnectionService(
    CloudStorageDbContext db,
    IHttpClientFactory clients,
    IServiceProvider services,
    CloudProviderOAuthStateStore states,
    IOptions<DropboxOptions> options
) : IDropboxConnectionService
{
    public const string ProviderId = "dropbox";
    public const string Scopes = "account_info.read files.content.write";
    private const string OAuthEndpoint = "https://api.dropbox.com/oauth2";
    private const string ApiEndpoint = "https://api.dropboxapi.com/2";

    public async Task<string> StartAsync(Guid tenantId, Guid userId, string? returnOrigin, CancellationToken ct)
    {
        var config = options.Value;
        EnsureConfigured(config);
        // Sin Encryption:MasterKey el token no se puede guardar: fallar aquí, antes del consentimiento.
        _ = services.GetRequiredService<ISecretProtector>();
        var state = await states.IssueAsync(tenantId, userId, ProviderId, returnOrigin, ct);
        var values = new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["redirect_uri"] = config.RedirectUri,
            ["response_type"] = "code",
            ["token_access_type"] = "offline",
            ["scope"] = Scopes,
            ["state"] = state.Id.ToString("N"),
        };
        return "https://www.dropbox.com/oauth2/authorize?"
            + string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    public async Task CompleteAsync(Guid stateId, string code, CancellationToken ct)
    {
        var state =
            await states.ConsumeAsync(stateId, ProviderId, ct)
            ?? throw new InvalidOperationException("The Dropbox authorization request expired. Please reconnect.");

        var config = options.Value;
        EnsureConfigured(config);
        using var tokenResponse = await clients
            .CreateClient("dropbox-oauth")
            .PostAsync(
                $"{OAuthEndpoint}/token",
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
            ?? throw new InvalidOperationException("Dropbox did not return an OAuth token.");
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException(
                "Dropbox did not return offline access. Revoke the existing grant and reconnect."
            );

        using var profileRequest = new HttpRequestMessage(HttpMethod.Post, $"{ApiEndpoint}/users/get_current_account")
        {
            Content = JsonContent.Create((object?)null),
        };
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var profileResponse = await clients.CreateClient("dropbox-api").SendAsync(profileRequest, ct);
        profileResponse.EnsureSuccessStatusCode();
        var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileResponse>(cancellationToken: ct);
        if (string.IsNullOrWhiteSpace(profile?.Email))
            throw new InvalidOperationException("Dropbox did not return the account email address.");

        var existing = await db
            .CloudConnections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == state.TenantId && x.Provider == ProviderId, ct);
        if (existing is not null)
            db.CloudConnections.Remove(existing);
        var protector = services.GetRequiredService<ISecretProtector>();
        db.CloudConnections.Add(
            TenantCloudConnection.Connect(
                state.TenantId,
                ProviderId,
                profile.Email,
                protector.Protect(token.RefreshToken)
            )
        );
        await db.SaveChangesAsync(ct);
    }

    public async Task<(bool Connected, string? Email)> GetStatusAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await db
            .CloudConnections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Provider == ProviderId, ct);
        return (connection is not null, connection?.AccountEmail);
    }

    // Dropbox consent is managed in the user's Dropbox account; disconnect removes the
    // tenant's encrypted credential and does not touch its cloud files.
    public async Task DisconnectAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await db.CloudConnections.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Provider == ProviderId,
            ct
        );
        if (connection is null)
            return;
        db.CloudConnections.Remove(connection);
        await db.SaveChangesAsync(ct);
    }

    private static void EnsureConfigured(DropboxOptions config)
    {
        if (
            string.IsNullOrWhiteSpace(config.ClientId)
            || string.IsNullOrWhiteSpace(config.ClientSecret)
            || string.IsNullOrWhiteSpace(config.RedirectUri)
            || string.IsNullOrWhiteSpace(config.FrontendReturnUri)
        )
            throw new InvalidOperationException("Dropbox OAuth is not configured for this environment.");
        if (!IsAllowedRedirect(config.RedirectUri) || !IsAllowedRedirect(config.FrontendReturnUri))
            throw new InvalidOperationException("Dropbox OAuth URLs must use HTTPS, except for localhost development.");
    }

    private static bool IsAllowedRedirect(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken
    );

    private sealed record ProfileResponse([property: JsonPropertyName("email")] string? Email);
}
